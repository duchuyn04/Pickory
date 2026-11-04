using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Modules.SupportDesk.Scheduling;
using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Sessions;

// Mở phiên chờ cho hội thoại chưa có phiên mở, kèm tin hệ thống báo khách.
// Bên gọi đã mở transaction và truyền mốc giờ. Phiên và tin hệ thống cùng commit hoặc cùng hủy với tin của khách.
internal sealed class SupportSessionOpener(
    SupportDeskDbContext database,
    SupportSettingsReader settingsReader,
    StaffPresenceService staffPresence)
{
    // Tên savepoint của bước chèn phiên. Tên cố định vì mỗi transaction chỉ chèn tối đa một phiên.
    private const string SavepointName = "support_session_open";

    // Chỉ mục unique một phần của phiên mở. Chỉ lỗi của chỉ mục này mới là do request khác thắng.
    private const string OpenSessionIndexName = "support_sessions_conversationId_open_key";

    // Số ngày lấy mẫu thời gian chờ trung bình. Đủ dài để san số liệu nhiễu, đủ ngắn để phản ánh tình hình hiện tại.
    private const int AverageWaitDays = 7;

    // Trả phiên vừa mở, hoặc `null` khi hội thoại đã có phiên mở hoặc request khác vừa mở phiên trước.
    public async Task<SupportSession?> OpenIfMissingAsync(
        SupportConversation conversation,
        DateTimeOffset nowMoment,
        DomainEventCollector events,
        CancellationToken cancellationToken)
    {
        var now = nowMoment.UtcDateTime;
        // Phiên mở là phiên chưa đóng. Có phiên mở rồi thì khách chỉ gửi thêm tin vào phiên đó.
        var hasOpenSession = await database.Sessions.AsNoTracking()
            .AnyAsync(
                session => session.ConversationId == conversation.Id && session.ClosedAt == null, cancellationToken);
        if (hasOpenSession)
        {
            return null;
        }

        // Số nhân viên sẵn sàng vừa quyết định "trong giờ", vừa là mẫu số của ước tính thời gian chờ.
        var availableStaffIds = await staffPresence.ListAvailableStaffIdsAsync(cancellationToken);
        var settings = await settingsReader.ReadAsync(cancellationToken);
        var inHours = WorkingHoursCalendar.IsWithinHours(nowMoment, settings.ToWorkingHours())
            && availableStaffIds.Count > 0;

        // Chỉ ước tính khi khách đang chờ trong giờ. Ngoài giờ thì con số này không được dùng tới.
        var estimateMinutes = inHours
            ? WaitEstimator.Estimate(
                await ReadAverageWaitAsync(now, cancellationToken),
                await CountWaitingSessionsAsync(cancellationToken),
                availableStaffIds.Count)
            : 0;

        var session = new SupportSession
        {
            ConversationId = conversation.Id,
            HandoffAt = now,
            HandoffInHours = inHours,
            // Hai cột định danh chép từ hội thoại đúng lúc mở phiên, để xóa dữ liệu khi module nhận `UserDeletedEvent`.
            CustomerUserId = conversation.CustomerUserId,
            AnonymousId = conversation.AnonymousId,
        };
        database.Sessions.Add(session);

        // Hai tin của khách đến cùng lúc cùng thấy chưa có phiên. Savepoint cho request thua quay lại rồi vẫn lưu tin của khách.
        // Transaction do bên gọi mở, nên opener dùng transaction đang chạy thay vì mở transaction riêng.
        var transaction = database.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Bước mở phiên phải chạy trong transaction của tin khách.");
        await transaction.CreateSavepointAsync(SavepointName, cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsOpenSessionConflict(exception))
        {
            // Postgres hủy cả transaction sau lỗi unique, nên phải quay về savepoint trước khi ghi tiếp.
            await transaction.RollbackToSavepointAsync(SavepointName, cancellationToken);
            // Bỏ phiên chưa lưu để lần lưu kế tiếp của transaction không thử chèn lại dòng đã thua.
            database.Entry(session).State = EntityState.Detached;
            return null;
        }

        var systemMessage = new SupportMessage
        {
            ConversationId = conversation.Id,
            Role = SupportMessageRole.System,
            Body = inHours
                ? SupportSystemMessages.WaitingInHours(estimateMinutes)
                : SupportSystemMessages.OutsideHours(settings.OpensAt, settings.ClosesAt),
            // Chỉ tin ngoài giờ mở form số điện thoại. Tin trong giờ không cần khách nhập gì.
            Action = inHours ? null : SupportSystemMessages.CallbackAction,
            // Tin hệ thống ghi sau tin của khách một mili giây. Hai tin cùng giờ thì lịch sử xếp theo mã, nên thứ tự sẽ ngẫu nhiên.
            CreatedAt = now.AddMilliseconds(1),
        };
        database.Messages.Add(systemMessage);
        await database.SaveChangesAsync(cancellationToken);

        // Sự kiện vào hàng đợi của transaction ngoài, chỉ phát sau khi transaction đó commit.
        // Kênh khách đổi trạng thái rồi hiện tin, kênh nhân viên báo có phiên mới vào hàng chờ.
        events.Emit(new SupportSessionStatusChangedEvent(
            session.Id, conversation.Id, SupportSessionStatus.Waiting));
        events.Emit(new SupportMessageCreatedEvent(
            conversation.Id,
            systemMessage.Id,
            SupportMessageCreatedEvent.SystemRole,
            systemMessage.Body,
            systemMessage.Action,
            systemMessage.CreatedAt));
        return session;
    }

    // Trung bình `ClaimedAt - HandoffAt` của các phiên được nhận trong 7 ngày gần nhất.
    private async Task<TimeSpan?> ReadAverageWaitAsync(DateTime now, CancellationToken cancellationToken)
    {
        var since = now.AddDays(-AverageWaitDays);
        var claimedWaits = await database.Sessions.AsNoTracking()
            .Where(session => session.ClaimedAt != null && session.ClaimedAt >= since)
            .Select(session => new { session.HandoffAt, session.ClaimedAt })
            .ToListAsync(cancellationToken);
        // Chưa có phiên nào được nhận trong 7 ngày thì chưa biết một lượt kéo dài bao lâu. `WaitEstimator` dùng giá trị mặc định.
        if (claimedWaits.Count == 0)
        {
            return null;
        }

        var totalTicks = claimedWaits.Sum(wait => (wait.ClaimedAt!.Value - wait.HandoffAt).Ticks);
        // Chia đều cho số phiên. Làm tròn tới tick gần nhất để giữ phần lẻ dưới một phút.
        return TimeSpan.FromTicks((long)Math.Round((double)totalTicks / claimedWaits.Count));
    }

    // Phiên chờ khác: chưa đóng và chưa có nhân viên nhận. Phiên của request này chưa được chèn, nên không bị tính.
    private Task<int> CountWaitingSessionsAsync(CancellationToken cancellationToken) =>
        database.Sessions.AsNoTracking()
            .CountAsync(session => session.ClosedAt == null && session.StaffId == null, cancellationToken);

    // Chỉ lỗi unique của chỉ mục phiên mở là tranh chấp mong đợi. Lỗi DB khác phải nổi ra ngoài.
    private static bool IsOpenSessionConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OpenSessionIndexName,
        };
}
