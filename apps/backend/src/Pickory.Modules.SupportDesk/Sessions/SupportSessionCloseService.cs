using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.SupportDesk.Sessions;

// Dịch vụ kết thúc phiên hỗ trợ CSKH cho nhân viên.
internal sealed class SupportSessionCloseService(
    SupportDeskDbContext database,
    TimeProvider timeProvider,
    IAuditLogWriter auditLogWriter,
    IEventDispatcher dispatcher)
{
    private const string NotFoundMessage = "Không tìm thấy phiên hỗ trợ.";
    private const string ClosedMessage = "Phiên hỗ trợ đã kết thúc.";
    private const string NotAssignedMessage = "Bạn chưa nhận hội thoại này nên không kết thúc phiên được.";
    private const string AuditAction = "support.session.close";
    private const string AuditTargetType = "support_session";

    // Thực hiện kết thúc phiên trong transaction gồm cập nhật phiên, thêm tin CSAT và ghi audit.
    public async Task<SupportSessionCloseResponse> CloseAsync(
        string sessionId,
        string callerStaffId,
        CancellationToken cancellationToken)
    {
        // Đọc phiên để kiểm tra sự tồn tại và quyền đóng phiên.
        var session = await database.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            throw new AppException(SupportDeskErrorCodes.SessionNotFound, 404, NotFoundMessage);
        }

        // Phiên đã kết thúc không cho phép kết thúc lần hai.
        if (session.ClosedAt is not null)
        {
            throw new AppException(SupportDeskErrorCodes.SessionClosed, 409, ClosedMessage);
        }

        // Chỉ người đang giữ phiên mới có quyền kết thúc phiên.
        if (session.StaffId != callerStaffId)
        {
            throw new AppException(SupportDeskErrorCodes.SessionNotAssigned, 403, NotAssignedMessage);
        }

        var nowMoment = timeProvider.GetUtcNow();
        var rawNow = nowMoment.UtcDateTime;
        // Cắt bớt phần lẻ 100 ns để khớp độ chính xác micro giây của Postgres.
        var now = rawNow.AddTicks(-(rawNow.Ticks % 10));
        var closedUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);

        var events = new DomainEventCollector(dispatcher);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Chạy UPDATE có điều kiện để đảm bảo chỉ kết thúc phiên đang mở của đúng người gọi.
        var updatedRows = await database.Sessions
            .Where(s => s.Id == sessionId && s.StaffId == callerStaffId && s.ClosedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.ClosedAt, now)
                .SetProperty(s => s.ClosedBy, callerStaffId), cancellationToken);
        if (updatedRows == 0)
        {
            // Đọc lại phiên để phân biệt chính xác nguyên nhân lỗi.
            var existingSession = await database.Sessions.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
            if (existingSession is null)
            {
                throw new AppException(SupportDeskErrorCodes.SessionNotFound, 404, NotFoundMessage);
            }

            // Kiểm tra `ClosedAt` trước `StaffId` để báo đúng trạng thái phiên đã kết thúc.
            if (existingSession.ClosedAt is not null)
            {
                throw new AppException(SupportDeskErrorCodes.SessionClosed, 409, ClosedMessage);
            }

            // Phiên còn mở nhưng người gọi không còn giữ phiên do đã chuyển giao.
            throw new AppException(SupportDeskErrorCodes.SessionNotAssigned, 403, NotAssignedMessage);
        }

        // Thêm tin nhắn hệ thống gửi tới khách mời chấm điểm CSAT.
        var systemMessage = new SupportMessage
        {
            ConversationId = session.ConversationId,
            Role = SupportMessageRole.System,
            Body = SupportSystemMessages.SessionClosed,
            Action = SupportSystemMessages.CsatAction,
            SessionId = sessionId,
            CreatedAt = now,
        };
        database.Messages.Add(systemMessage);

        // Cập nhật mốc hoạt động mới nhất của hội thoại.
        await database.Conversations
            .Where(c => c.Id == session.ConversationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.LastMessageAt, now), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        // Ghi nhật ký kiểm toán trong cùng transaction để đảm bảo toàn vẹn dữ liệu.
        var before = new { closedAt = (DateTime?)null, closedBy = (string?)null };
        var after = new { closedAt = (DateTime?)closedUtc, closedBy = callerStaffId };
        var auditEntry = new AuditLogEntry(
            callerStaffId,
            AuditAction,
            AuditTargetType,
            sessionId,
            before,
            after);
        await auditLogWriter.WriteAsync(auditEntry, transaction.GetDbTransaction(), cancellationToken);

        // Commit transaction trước khi phát sự kiện miền để đảm bảo dữ liệu bền vững.
        await transaction.CommitAsync(cancellationToken);

        // Phát sự kiện miền sau khi commit để khách và nhân viên cập nhật giao diện.
        events.Emit(new SupportSessionStatusChangedEvent(
            sessionId, session.ConversationId, SupportSessionStatus.Closed));
        events.Emit(new SupportMessageCreatedEvent(
            session.ConversationId,
            systemMessage.Id,
            SupportMessageCreatedEvent.SystemRole,
            systemMessage.Body,
            systemMessage.Action,
            systemMessage.CreatedAt,
            StaffDisplayName: null,
            SessionId: sessionId));
        await events.FlushAsync(cancellationToken);

        return new SupportSessionCloseResponse(sessionId, closedUtc, callerStaffId);
    }
}
