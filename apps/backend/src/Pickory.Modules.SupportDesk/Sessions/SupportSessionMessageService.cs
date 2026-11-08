using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Staff;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.SupportDesk.Sessions;

// Dịch vụ nhân viên gửi tin nhắn trong phiên hỗ trợ.
internal sealed class SupportSessionMessageService(
    SupportDeskDbContext database,
    IStaffDirectory staffDirectory,
    TimeProvider timeProvider,
    IEventDispatcher dispatcher)
{
    private const string NotFoundMessage = "Không tìm thấy phiên hỗ trợ.";
    private const string ClosedMessage = "Phiên hỗ trợ đã kết thúc.";
    private const string NotAssignedMessage = "Bạn chưa nhận hội thoại này nên không gửi tin được.";

    // Gửi tin nhắn vai staff vào phiên hỗ trợ đang phụ trách.
    public async Task<StaffViewMessageResponse> SendMessageAsync(
        string sessionId, string staffId, string trimmedBody, CancellationToken cancellationToken)
    {
        // Kiểm tra phiên tồn tại trước khi xử lý gửi tin.
        var session = await database.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            throw new AppException(SupportDeskErrorCodes.SessionNotFound, 404, NotFoundMessage);
        }

        // Phiên đã kết thúc không nhận thêm tin nhắn mới.
        if (session.ClosedAt is not null)
        {
            throw new AppException(SupportDeskErrorCodes.SessionClosed, 409, ClosedMessage);
        }

        // Người gọi không giữ phiên này thì từ chối quyền gửi tin.
        if (session.StaffId != staffId)
        {
            throw new AppException(SupportDeskErrorCodes.SessionNotAssigned, 403, NotAssignedMessage);
        }

        var nowMoment = timeProvider.GetUtcNow();
        var rawNow = nowMoment.UtcDateTime;
        // Cắt bớt phần lẻ 100 ns để khớp độ chính xác micro giây của Postgres.
        var now = rawNow.AddTicks(-(rawNow.Ticks % 10));

        // Tra tên hiển thị của nhân viên từ danh bạ không qua email dự phòng.
        var names = await staffDirectory.GetProfileNamesAsync([staffId], cancellationToken);
        string? staffDisplayName = null;
        if (names.TryGetValue(staffId, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            staffDisplayName = name;
        }

        var message = new SupportMessage
        {
            ConversationId = session.ConversationId,
            Role = SupportMessageRole.Staff,
            StaffId = staffId,
            SessionId = session.Id,
            Body = trimmedBody,
            CreatedAt = now,
        };

        var events = new DomainEventCollector(dispatcher);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Cập nhật mốc phản hồi đầu tiên nguyên tử khi phiên chưa có mốc này.
        await database.Sessions
            .Where(s => s.Id == sessionId && s.FirstResponseAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.FirstResponseAt, now), cancellationToken);

        // Lưu tin nhắn và cập nhật mốc hoạt động mới nhất của hội thoại.
        database.Messages.Add(message);
        await database.Conversations
            .Where(c => c.Id == session.ConversationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.LastMessageAt, now), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        // Commit transaction trước khi phát sự kiện miền để đảm bảo dữ liệu bền vững.
        await transaction.CommitAsync(cancellationToken);

        // Phát sự kiện miền tới khách qua kênh SSE sau khi commit thành công.
        events.Emit(new SupportMessageCreatedEvent(
            session.ConversationId,
            message.Id,
            SupportMessageCreatedEvent.StaffRole,
            message.Body,
            message.Action,
            message.CreatedAt,
            StaffDisplayName: staffDisplayName,
            SessionId: session.Id));
        await events.FlushAsync(cancellationToken);

        return StaffViewMessageResponse.From(message, staffDisplayName);
    }
}
