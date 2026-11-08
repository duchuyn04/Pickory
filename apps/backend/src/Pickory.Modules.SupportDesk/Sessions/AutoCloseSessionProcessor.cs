using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Scheduling;
using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Sessions;

// Bộ xử lý tự đóng các phiên im lặng và các phiên chờ ngoài giờ không có người nhận.
internal sealed class AutoCloseSessionProcessor(
    SupportDeskDbContext database,
    SupportSettingsReader settingsReader,
    TimeProvider timeProvider,
    IEventDispatcher dispatcher,
    ILogger<AutoCloseSessionProcessor> logger)
{
    private const string AutoClosedBy = "auto";

    // Tự đóng các phiên hết hạn hoạt động và trả về số lượng phiên đã đóng.
    public async Task<int> CloseInactiveSessionsAsync(CancellationToken cancellationToken)
    {
        var nowMoment = timeProvider.GetUtcNow();
        var rawNow = nowMoment.UtcDateTime;
        // Cắt bớt phần lẻ 100 ns để khớp độ chính xác micro giây của Postgres.
        var now = rawNow.AddTicks(-(rawNow.Ticks % 10));

        var settings = await settingsReader.ReadAsync(cancellationToken);
        // Mốc thời gian ngưỡng: hoạt động cũ hơn mốc này sẽ bị xem là hết hạn.
        var thresholdMoment = now.AddMinutes(-settings.AutoCloseMinutes);

        // Lấy các phiên cần đóng: phiên chờ ngoài giờ hoặc phiên nhân viên im lặng quá hạn.
        var candidates = await database.Sessions.AsNoTracking()
            .Where(session => session.ClosedAt == null && session.HandoffAt <= thresholdMoment)
            .Where(session =>
                (session.StaffId == null && !session.HandoffInHours)
                || (session.StaffId != null && !database.Messages.Any(m => m.SessionId == session.Id && m.CreatedAt > thresholdMoment)))
            .OrderBy(session => session.HandoffAt)
            .ThenBy(session => session.Id)
            .Select(session => new
            {
                session.Id,
                session.ConversationId,
                IsClaimed = session.StaffId != null,
            })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var events = new DomainEventCollector(dispatcher);
        var closedCount = 0;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            // Chạy UPDATE có điều kiện kiểm tra lại trạng thái để tránh xung đột.
            int updatedRows;
            if (candidate.IsClaimed)
            {
                // Với phiên đã nhận, phải giữ `StaffId` khác `null` và không có tin mới sau ngưỡng.
                updatedRows = await database.Sessions
                    .Where(s => s.Id == candidate.Id
                        && s.ClosedAt == null
                        && s.StaffId != null
                        && s.HandoffAt <= thresholdMoment
                        && !database.Messages.Any(m => m.SessionId == s.Id && m.CreatedAt > thresholdMoment))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.ClosedAt, now)
                        .SetProperty(s => s.ClosedBy, AutoClosedBy), cancellationToken);
            }
            else
            {
                // Với phiên chờ ngoài giờ, phải giữ chưa ai nhận và phiên không trong giờ.
                updatedRows = await database.Sessions
                    .Where(s => s.Id == candidate.Id
                        && s.ClosedAt == null
                        && s.StaffId == null
                        && !s.HandoffInHours
                        && s.HandoffAt <= thresholdMoment)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.ClosedAt, now)
                        .SetProperty(s => s.ClosedBy, AutoClosedBy), cancellationToken);
            }
            if (updatedRows == 0)
            {
                continue;
            }

            // Thêm tin nhắn hệ thống mời khách hàng chấm điểm CSAT.
            var systemMessage = new SupportMessage
            {
                ConversationId = candidate.ConversationId,
                Role = SupportMessageRole.System,
                Body = SupportSystemMessages.SessionClosed,
                Action = SupportSystemMessages.CsatAction,
                SessionId = candidate.Id,
                CreatedAt = now,
            };
            database.Messages.Add(systemMessage);

            // Cập nhật mốc thời gian hoạt động mới nhất của hội thoại.
            await database.Conversations
                .Where(c => c.Id == candidate.ConversationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.LastMessageAt, now), cancellationToken);
            await database.SaveChangesAsync(cancellationToken);

            // Ghi nhật ký hệ thống cấp thông tin và không ghi audit log cho tự đóng.
            logger.LogInformation(
                "Tự đóng phiên {SessionId} của hội thoại {ConversationId}. closedBy = {ClosedBy}.",
                candidate.Id,
                candidate.ConversationId,
                AutoClosedBy);

            // Gom sự kiện để phát tới khách hàng và nhân viên sau khi commit giao dịch.
            events.Emit(new SupportSessionStatusChangedEvent(
                candidate.Id, candidate.ConversationId, SupportSessionStatus.Closed));
            events.Emit(new SupportMessageCreatedEvent(
                candidate.ConversationId,
                systemMessage.Id,
                SupportMessageCreatedEvent.SystemRole,
                systemMessage.Body,
                systemMessage.Action,
                systemMessage.CreatedAt,
                StaffDisplayName: null,
                SessionId: candidate.Id));

            closedCount++;
        }

        // Commit giao dịch trước khi phát sự kiện miền để đảm bảo dữ liệu đã lưu.
        await transaction.CommitAsync(cancellationToken);
        await events.FlushAsync(cancellationToken);

        return closedCount;
    }
}
