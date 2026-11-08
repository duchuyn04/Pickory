using Microsoft.EntityFrameworkCore;
using Pickory.Modules.SupportDesk.Contracts.Events;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Scheduling;
using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Sessions;

// Job nhắc lại các phiên chờ quá lâu trong giờ. Mỗi lần nhắc ghi tin hệ thống có form số điện thoại.
// Job cũng báo nhân viên sẵn sàng. Mỗi phiên chỉ nhắc một lần nhờ cột `nudgedAt`, nên hai vòng chạy chồng nhau không gửi hai tin.
internal sealed class WaitNudgeProcessor(
    SupportDeskDbContext database,
    SupportSettingsReader settingsReader,
    TimeProvider timeProvider,
    IEventDispatcher dispatcher)
{
    // Trả số phiên vừa được nhắc trong lần chạy này.
    public async Task<int> RemindWaitingSessionsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var settings = await settingsReader.ReadAsync(cancellationToken);
        // Mốc nửa mở: phiên chờ đúng `WaitNudgeMinutes` phút đã tới lúc nhắc, chưa tới thì để vòng sau.
        var remindBefore = now.AddMinutes(-settings.WaitNudgeMinutes);

        // Phiên cần nhắc là phiên còn trong hàng chờ, mở trong giờ và chưa từng được nhắc.
        var candidates = await database.Sessions.AsNoTracking()
            .Where(session => session.ClosedAt == null
                && session.StaffId == null
                && session.HandoffInHours
                && session.NudgedAt == null
                && session.HandoffAt <= remindBefore)
            // Thứ tự cố định để hai lần chạy cùng dữ liệu xử lý theo cùng thứ tự.
            .OrderBy(session => session.HandoffAt)
            .ThenBy(session => session.Id)
            .Select(session => new { session.Id, session.ConversationId })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Sự kiện gom trong lúc transaction chạy và chỉ phát sau commit.
        var events = new DomainEventCollector(dispatcher);
        var remindedCount = 0;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            // Phiên được nhận hoặc bị đóng sau bước đọc thì không bị nhắc.
            // Điều kiện `nudgedAt` rỗng chặn vòng chạy chồng nhắc lần hai.
            var updatedRows = await database.Sessions
                .Where(session => session.Id == candidate.Id
                    && session.ClosedAt == null
                    && session.StaffId == null
                    && session.HandoffInHours
                    && session.NudgedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.NudgedAt, now), cancellationToken);
            if (updatedRows == 0)
            {
                continue;
            }

            var reminder = new SupportMessage
            {
                ConversationId = candidate.ConversationId,
                Role = SupportMessageRole.System,
                Body = SupportSystemMessages.WaitReminder,
                Action = SupportSystemMessages.CallbackAction,
                SessionId = candidate.Id,
                // Tin nhắc lại muộn hơn 1 mili giây. Tin của khách ghi cùng mốc giờ thì lịch sử xếp tin khách trước.
                CreatedAt = now.AddMilliseconds(1),
            };
            database.Messages.Add(reminder);
            // Tin nhắc lại là hoạt động mới nhất của hội thoại. Hạn lưu 90 ngày và hạn đóng im lặng tính lại từ mốc này.
            await database.Conversations
                .Where(conversation => conversation.Id == candidate.ConversationId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(conversation => conversation.LastMessageAt, now), cancellationToken);
            await database.SaveChangesAsync(cancellationToken);

            // Tin nhắc lại đi tới kênh của khách, sự kiện nhắc lại đi tới kênh của nhân viên sẵn sàng.
            events.Emit(new SupportMessageCreatedEvent(
                candidate.ConversationId,
                reminder.Id,
                SupportMessageCreatedEvent.SystemRole,
                reminder.Body,
                reminder.Action,
                reminder.CreatedAt,
                StaffDisplayName: null,
                SessionId: candidate.Id));
            events.Emit(new SupportSessionRemindedEvent(candidate.Id));
            remindedCount++;
        }

        await transaction.CommitAsync(cancellationToken);
        // Phát sau commit. Handler đọc DB bằng kết nối khác vẫn thấy tin nhắc lại.
        await events.FlushAsync(cancellationToken);
        return remindedCount;
    }
}
