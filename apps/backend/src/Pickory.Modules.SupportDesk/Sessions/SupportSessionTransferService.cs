using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.SupportDesk.Sessions;

// Dịch vụ chuyển phiên hỗ trợ CSKH cho đồng nghiệp.
internal sealed class SupportSessionTransferService(
    SupportDeskDbContext database,
    TimeProvider timeProvider,
    IAuditLogWriter auditLogWriter,
    IEventDispatcher dispatcher)
{
    private const string NotFoundMessage = "Không tìm thấy phiên hỗ trợ.";
    private const string ClosedMessage = "Phiên hỗ trợ đã kết thúc.";
    private const string NotAssignedMessage = "Bạn chưa nhận hội thoại này nên không chuyển phiên được.";
    private const string StaffUnavailableMessage = "Người nhận không ở trạng thái sẵn sàng.";
    private const string AuditAction = "support.session.transfer";
    private const string AuditTargetType = "support_session";

    // Thực hiện chuyển phiên trong transaction gồm cập nhật phiên và ghi nhật ký kiểm toán.
    public async Task<SupportSessionClaimResponse> TransferAsync(
        string sessionId,
        string callerStaffId,
        string targetStaffId,
        string? note,
        CancellationToken cancellationToken)
    {
        // Kiểm tra phiên tồn tại trước khi xử lý chuyển phiên.
        var session = await database.Sessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            throw new AppException(SupportDeskErrorCodes.SessionNotFound, 404, NotFoundMessage);
        }

        // Phiên đã kết thúc không cho phép chuyển cho người khác.
        if (session.ClosedAt is not null)
        {
            throw new AppException(SupportDeskErrorCodes.SessionClosed, 409, ClosedMessage);
        }

        // Chỉ nhân viên đang giữ phiên mới có quyền chuyển cho đồng nghiệp.
        if (session.StaffId != callerStaffId)
        {
            throw new AppException(SupportDeskErrorCodes.SessionNotAssigned, 403, NotAssignedMessage);
        }

        // Người nhận phải có dòng trực ở trạng thái sẵn sàng.
        var isReceiverAvailable = await database.StaffPresences.AsNoTracking()
            .AnyAsync(p => p.StaffId == targetStaffId && p.Status == StaffPresenceStatus.Available, cancellationToken);
        if (!isReceiverAvailable)
        {
            throw new AppException(SupportDeskErrorCodes.StaffUnavailable, 409, StaffUnavailableMessage);
        }

        var nowMoment = timeProvider.GetUtcNow();
        var rawNow = nowMoment.UtcDateTime;
        // Cắt bớt phần lẻ 100 ns để khớp độ chính xác micro giây của Postgres.
        var now = rawNow.AddTicks(-(rawNow.Ticks % 10));
        var claimedUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc);

        var events = new DomainEventCollector(dispatcher);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Chạy UPDATE có điều kiện để chuyển giao phiên nguyên tử.
        var updatedRows = await database.Sessions
            .Where(s => s.Id == sessionId && s.StaffId == callerStaffId && s.ClosedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.StaffId, targetStaffId)
                .SetProperty(s => s.ClaimedAt, now)
                .SetProperty(s => s.TransferNote, note), cancellationToken);
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

        // Ghi nhật ký kiểm toán trong cùng transaction để đảm bảo toàn vẹn dữ liệu.
        var before = new { staffId = callerStaffId, transferNote = session.TransferNote };
        var after = new { staffId = targetStaffId, transferNote = note };
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

        // Phát sự kiện miền sau khi commit để nhân viên cập nhật danh sách hàng chờ.
        events.Emit(new SupportSessionStatusChangedEvent(
            sessionId, session.ConversationId, SupportSessionStatus.Assigned));
        await events.FlushAsync(cancellationToken);

        return new SupportSessionClaimResponse(sessionId, targetStaffId, claimedUtc);
    }
}
