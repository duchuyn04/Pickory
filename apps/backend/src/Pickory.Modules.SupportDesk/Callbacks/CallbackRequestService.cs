using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Conversations;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Calendar;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.SupportDesk.Callbacks;

// Tạo, liệt kê và cập nhật yêu cầu gọi lại. Route đã kiểm quyền và validator trước khi gọi đến đây.
internal sealed class CallbackRequestService(
    SupportDeskDbContext database,
    TimeProvider timeProvider,
    IEventDispatcher dispatcher,
    ILogger<CallbackRequestService> logger)
{
    public const int DailyLimit = 3;

    private const string ConversationNotFoundMessage = "Không tìm thấy hội thoại.";
    private const string CallbackRequestNotFoundMessage = "Không tìm thấy yêu cầu gọi lại.";
    private const string LimitReachedMessage = "Bạn đã gửi 3 yêu cầu gọi lại hôm nay. CSKH sẽ liên hệ sớm.";
    private const string AutoClosedBy = "auto";

    public async Task<CallbackRequestCreatedResponse> CreateAsync(
        Actor actor, string conversationId, string normalizedPhone, string? note, CancellationToken cancellationToken)
    {
        // Kiểm chủ trước khi mở transaction. Hội thoại của người khác và `id` không tồn tại cùng nhận 404.
        var isOwner = await ConversationOwnership.OwnedBy(database.Conversations.AsNoTracking(), actor)
            .AnyAsync(conversation => conversation.Id == conversationId, cancellationToken);
        if (!isOwner)
        {
            throw new AppException(SupportDeskErrorCodes.ConversationNotFound, 404, ConversationNotFoundMessage);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Sự kiện gom trong lúc transaction chạy và chỉ phát sau commit. Transaction hủy không phát sự kiện nào.
        var events = new DomainEventCollector(dispatcher);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Khóa mức transaction theo `anonymousId`, để hai request song song không cùng vượt giới hạn ngày.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({actor.AnonymousId}))", cancellationToken);

        // Ngày giờ Việt Nam chứa mốc hiện tại. Hai cận nửa mở nên yêu cầu lúc 23:59:59.999 vẫn thuộc đúng ngày.
        var today = VietnamCalendar.ToVietnamDate(now);
        var dayStartUtc = VietnamCalendar.DayStartUtc(today);
        var dayEndUtc = VietnamCalendar.DayEndExclusiveUtc(today);
        var requestCount = await database.CallbackRequests.AsNoTracking()
            .CountAsync(request => request.AnonymousId == actor.AnonymousId
                && request.CreatedAt >= dayStartUtc
                && request.CreatedAt < dayEndUtc, cancellationToken);
        if (requestCount >= DailyLimit)
        {
            // Yêu cầu bị chặn không lưu dòng và không đóng phiên chờ.
            throw new AppException(SupportDeskErrorCodes.CallbackLimitReached, 429, LimitReachedMessage);
        }

        // Lưu cả hai mã định danh để đếm giới hạn theo ngày và xóa số khi khách xóa tài khoản.
        var request = new CallbackRequest
        {
            ConversationId = conversationId,
            AnonymousId = actor.AnonymousId,
            CustomerUserId = actor.UserId,
            Phone = normalizedPhone,
            Note = note,
            Status = CallbackRequestStatus.Pending,
            CreatedAt = now,
        };
        database.CallbackRequests.Add(request);

        // Phiên chờ đóng trong cùng transaction với yêu cầu, để khách không thấy yêu cầu gọi lại mà phiên vẫn chờ.
        await CloseWaitingSessionAsync(conversationId, now, events, cancellationToken);

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Phát sau commit. Handler đọc DB bằng kết nối khác vẫn thấy dòng yêu cầu mới.
        events.Emit(new CallbackRequestCreatedEvent(request.Id));
        await events.FlushAsync(cancellationToken);
        return CallbackRequestCreatedResponse.From(request);
    }

    public async Task<IReadOnlyList<CallbackRequestResponse>> ListAsync(CancellationToken cancellationToken)
    {
        // Xếp `pending` lên đầu cho nhân viên gọi trước, rồi `createdAt` mới nhất trước và phá thế hòa theo `id`.
        var rows = await database.CallbackRequests.AsNoTracking()
            .OrderBy(request => request.Status == CallbackRequestStatus.Pending ? 0 : 1)
            .ThenByDescending(request => request.CreatedAt)
            .ThenBy(request => request.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(CallbackRequestResponse.From).ToList();
    }

    public async Task<CallbackRequestResponse> UpdateStatusAsync(
        string callbackRequestId, string staffId, CallbackRequestStatus status, CancellationToken cancellationToken)
    {
        var request = await database.CallbackRequests
            .SingleOrDefaultAsync(row => row.Id == callbackRequestId, cancellationToken);
        if (request is null)
        {
            // Yêu cầu không có trả 404 mã riêng để giao diện hiện đúng thông báo cho nhân viên.
            throw new AppException(SupportDeskErrorCodes.CallbackRequestNotFound, 404, CallbackRequestNotFoundMessage);
        }

        // Ghi đè nhân viên và mốc gọi mỗi lần cập nhật để lưu người vừa liên hệ khách gần nhất.
        request.Status = status;
        request.HandledBy = staffId;
        request.HandledAt = timeProvider.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellationToken);
        return CallbackRequestResponse.From(request);
    }

    // Đóng phiên chờ chưa ai nhận của hội thoại. Không dòng nào cập nhật nghĩa là phiên vẫn mở, nên bỏ qua.
    private async Task CloseWaitingSessionAsync(
        string conversationId, DateTime now, DomainEventCollector events, CancellationToken cancellationToken)
    {
        // Đọc mã phiên mở cho sự kiện. Chỉ mục unique bảo đảm mỗi hội thoại có tối đa một phiên mở.
        var openSessionId = await database.Sessions.AsNoTracking()
            .Where(session => session.ConversationId == conversationId && session.ClosedAt == null)
            .Select(session => session.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (openSessionId is null)
        {
            return;
        }

        // Chỉ đóng khi phiên chưa có nhân viên nhận. Trả về 0 dòng nghĩa là phiên vừa được nhận hoặc đã đóng.
        var closedRows = await database.Sessions
            .Where(session => session.Id == openSessionId
                && session.ClosedAt == null
                && session.StaffId == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.ClosedAt, now)
                .SetProperty(session => session.ClosedBy, AutoClosedBy), cancellationToken);
        if (closedRows == 0)
        {
            return;
        }

        // Quyết định 19: phiên tự đóng không có nhân viên nên không ghi audit. Log giữ dấu vết để điều tra.
        logger.LogInformation(
            "Tự đóng phiên chờ {SessionId} khi khách gửi yêu cầu gọi lại. ClosedBy = {ClosedBy}.",
            openSessionId,
            AutoClosedBy);
        events.Emit(new SupportSessionStatusChangedEvent(
            openSessionId, conversationId, SupportSessionStatus.Closed));
    }
}
