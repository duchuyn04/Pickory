using Microsoft.EntityFrameworkCore;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Realtime;

namespace Pickory.Modules.SupportDesk.Presence;

// Chuyển nhân viên mất nhịp tim quá 2 phút sang `away`. Nhân viên đóng tab hoặc mất mạng không giữ phiên cho khách.
internal sealed class StaffPresenceTimeoutProcessor(
    SupportDeskDbContext database,
    SupportEventHub eventHub,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(2);

    // Trả số nhân viên vừa bị chuyển sang `away`.
    public async Task<int> MarkStaleStaffAwayAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Mốc là nửa mở: `lastSeenAt` cũ đúng 2 phút còn `available`, cũ hơn mới sang `away`. So sánh `<` giữ đúng biên này.
        var staleBefore = now - HeartbeatTimeout;
        var staleStaffIds = await database.StaffPresences
            .AsNoTracking()
            .Where(row => row.Status == StaffPresenceStatus.Available && row.LastSeenAt < staleBefore)
            .Select(row => row.StaffId)
            .ToListAsync(cancellationToken);

        var changedCount = 0;
        foreach (var staffId in staleStaffIds)
        {
            // Lặp lại điều kiện trong câu UPDATE. Nhịp tim đến giữa lúc đọc và ghi thì dòng không bị chuyển nhầm sang `away`.
            var updatedRows = await database.StaffPresences
                .Where(row => row.StaffId == staffId
                    && row.Status == StaffPresenceStatus.Available
                    && row.LastSeenAt < staleBefore)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, StaffPresenceStatus.Away), cancellationToken);
            if (updatedRows == 0)
            {
                continue;
            }

            // Phát sự kiện sau khi DB đã ghi. Chỉ phát cho dòng thật sự đổi, để client không nhận sự kiện thừa.
            eventHub.Broadcast(new SupportEvent(
                SupportEventNames.PresenceChanged,
                new PresenceChangedEventData(staffId, StaffPresenceWire.Away)));
            changedCount++;
        }

        return changedCount;
    }
}
