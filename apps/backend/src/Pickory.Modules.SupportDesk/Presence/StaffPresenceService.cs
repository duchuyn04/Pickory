using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.Modules.SupportDesk.Entities;
using Pickory.Modules.SupportDesk.Persistence;
using Pickory.Modules.SupportDesk.Realtime;

namespace Pickory.Modules.SupportDesk.Presence;

// Trạng thái trực của nhân viên trả cho client. `Status` là chuỗi `available` hoặc `away`.
internal sealed record StaffPresenceResponse(string StaffId, string Status, DateTime LastSeenAt);

// Đọc và ghi trạng thái trực. Mọi thay đổi `status` ghi DB xong mới phát sự kiện, để client nhận sự kiện không đọc phải dữ liệu cũ.
internal sealed class StaffPresenceService(
    SupportDeskDbContext database,
    SupportEventHub eventHub,
    TimeProvider timeProvider)
{
    public async Task<StaffPresenceResponse> SetStatusAsync(
        string staffId, StaffPresenceStatus status, CancellationToken cancellationToken)
    {
        var now = GetNowAtColumnPrecision();

        // Thử sửa dòng đã có trước. Nhân viên đã bật hoặc tắt trước đó không phải chèn dòng mới.
        var updatedRows = await UpdateRowAsync(staffId, status, now, cancellationToken);
        if (updatedRows == 0)
        {
            await InsertOrUpdateAfterRaceAsync(staffId, status, now, cancellationToken);
        }

        // Phát sự kiện sau khi DB đã ghi. Nhân viên khác nhận sự kiện rồi đọc lại danh sách sẽ thấy trạng thái mới.
        var response = new StaffPresenceResponse(staffId, StaffPresenceWire.ToWire(status), now);
        eventHub.Broadcast(new SupportEvent(
            SupportEventNames.PresenceChanged,
            new PresenceChangedEventData(staffId, response.Status)));
        return response;
    }

    public async Task<IReadOnlyList<StaffPresenceResponse>> ListAvailableAsync(CancellationToken cancellationToken)
    {
        // Chỉ nhân viên `available` mới nhận hội thoại. Thứ tự theo mã nhân viên để kết quả ổn định giữa các lần gọi.
        var rows = await database.StaffPresences
            .AsNoTracking()
            .Where(row => row.Status == StaffPresenceStatus.Available)
            .OrderBy(row => row.StaffId)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new StaffPresenceResponse(row.StaffId, StaffPresenceWire.ToWire(row.Status), row.LastSeenAt))
            .ToList();
    }

    // Nhịp tim chỉ làm mới `lastSeenAt` khi dòng đã có. Mở kết nối SSE không được tự bật nhân viên sang `available`.
    public async Task RecordHeartbeatAsync(string staffId, CancellationToken cancellationToken)
    {
        var now = GetNowAtColumnPrecision();
        await database.StaffPresences
            .Where(row => row.StaffId == staffId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.LastSeenAt, now), cancellationToken);
    }

    // Cột `lastSeenAt` chỉ lưu tới mili giây. Cắt giờ trước khi ghi để giá trị trả cho client bằng giá trị đọc lại từ DB.
    private DateTime GetNowAtColumnPrecision()
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    private Task<int> UpdateRowAsync(
        string staffId, StaffPresenceStatus status, DateTime now, CancellationToken cancellationToken)
    {
        return database.StaffPresences
            .Where(row => row.StaffId == staffId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.Status, status)
                    .SetProperty(row => row.LastSeenAt, now),
                cancellationToken);
    }

    private async Task InsertOrUpdateAfterRaceAsync(
        string staffId, StaffPresenceStatus status, DateTime now, CancellationToken cancellationToken)
    {
        database.StaffPresences.Add(new StaffPresence { StaffId = staffId, Status = status, LastSeenAt = now });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Hai tab của cùng một nhân viên chèn dòng đầu tiên cùng lúc. Request thua cuộc bỏ dòng chờ chèn rồi sửa dòng đã có.
            database.ChangeTracker.Clear();
            await UpdateRowAsync(staffId, status, now, cancellationToken);
        }
    }
}
