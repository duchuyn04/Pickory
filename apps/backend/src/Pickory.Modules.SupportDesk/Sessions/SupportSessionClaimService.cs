using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.SupportDesk.Sessions;

// Dịch vụ nhận phiên hỗ trợ cho nhân viên CSKH.
internal sealed class SupportSessionClaimService(
    SupportDeskDbContext database,
    TimeProvider timeProvider,
    IAuditLogWriter auditLogWriter,
    IEventDispatcher dispatcher)
{
    private const string NotFoundMessage = "Không tìm thấy phiên hỗ trợ.";
    private const string ClosedMessage = "Phiên hỗ trợ đã kết thúc.";
    private const string AlreadyClaimedMessage = "Đã có người nhận hội thoại này.";
    private const string AuditAction = "support.session.claim";
    private const string AuditTargetType = "support_session";

    // Thực hiện nhận phiên trong một giao dịch gồm cập nhật trạng thái, thêm tin và ghi audit.
    public async Task<SupportSessionClaimResponse> ClaimAsync(
        string sessionId, string staffId, CancellationToken cancellationToken)
    {
        var nowMoment = timeProvider.GetUtcNow();
        var rawNow = nowMoment.UtcDateTime;
        // Cắt bớt phần lẻ 100 ns để khớp độ chính xác micro giây của Postgres.
        var now = rawNow.AddTicks(-(rawNow.Ticks % 10));

        var events = new DomainEventCollector(dispatcher);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Chạy UPDATE có điều kiện để gán phiên nguyên tử mà không bị tranh chấp.
        var updatedRows = await database.Sessions
            .Where(session => session.Id == sessionId && session.StaffId == null && session.ClosedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.StaffId, staffId)
                .SetProperty(session => session.ClaimedAt, now), cancellationToken);

        // Số dòng cập nhật bằng 0 nghĩa là phiên không tồn tại, đã đóng hoặc đã có người nhận.
        if (updatedRows == 0)
        {
            // Đọc lại phiên để phân biệt chính xác nguyên nhân lỗi.
            var existingSession = await database.Sessions.AsNoTracking()
                .SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
            if (existingSession is null)
            {
                throw new AppException(SupportDeskErrorCodes.SessionNotFound, 404, NotFoundMessage);
            }

            // Kiểm tra ClosedAt trước StaffId. Phiên đã đóng có thể đã có người nhận, và nhân viên cần biết phiên đã kết thúc.
            if (existingSession.ClosedAt is not null)
            {
                throw new AppException(SupportDeskErrorCodes.SessionClosed, 409, ClosedMessage);
            }

            throw new AppException(SupportDeskErrorCodes.SessionAlreadyClaimed, 409, AlreadyClaimedMessage);
        }

        // Đọc ConversationId của phiên đã nhận để chèn tin nhắn hệ thống vào hội thoại.
        var conversationId = await database.Sessions.AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Select(session => session.ConversationId)
            .SingleAsync(cancellationToken);

        // Thêm tin hệ thống vai system báo nhân viên đã nhận và sẽ trả lời ngay.
        var systemMessage = new SupportMessage
        {
            ConversationId = conversationId,
            Role = SupportMessageRole.System,
            Body = SupportSystemMessages.SessionClaimed,
            Action = null,
            SessionId = sessionId,
            CreatedAt = now,
        };
        database.Messages.Add(systemMessage);
        await database.SaveChangesAsync(cancellationToken);

        // Ghi nhật ký kiểm toán trong cùng transaction để đảm bảo toàn vẹn dữ liệu.
        var claimedUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        var before = new { staffId = (string?)null, claimedAt = (DateTime?)null };
        var after = new { staffId, claimedAt = (DateTime?)claimedUtc };
        var auditEntry = new AuditLogEntry(
            staffId,
            AuditAction,
            AuditTargetType,
            sessionId,
            before,
            after);
        await auditLogWriter.WriteAsync(auditEntry, transaction.GetDbTransaction(), cancellationToken);

        // Commit transaction trước khi phát sự kiện miền để đảm bảo dữ liệu đã lưu bền vững.
        await transaction.CommitAsync(cancellationToken);

        // Phát sự kiện miền sau khi commit để khách và nhân viên khác cập nhật trạng thái.
        events.Emit(new SupportSessionStatusChangedEvent(
            sessionId, conversationId, SupportSessionStatus.Assigned));
        events.Emit(new SupportMessageCreatedEvent(
            conversationId,
            systemMessage.Id,
            SupportMessageCreatedEvent.SystemRole,
            systemMessage.Body,
            systemMessage.Action,
            systemMessage.CreatedAt,
            StaffDisplayName: null,
            SessionId: sessionId));
        await events.FlushAsync(cancellationToken);

        return new SupportSessionClaimResponse(sessionId, staffId, claimedUtc);
    }
}
