using Microsoft.EntityFrameworkCore;
using Pickory.Modules.ActivityTracking.Persistence;
using Pickory.Shared.Calendar;

namespace Pickory.Modules.ActivityTracking.Counters;

// Cộng 1 vào bộ đếm theo ngày. Route nhận lượt xem và các handler đếm thêm giỏ, đặt đơn dùng chung lớp này.
internal sealed class DailyCounterWriter(ActivityTrackingDbContext database, TimeProvider timeProvider)
{
    public async Task IncrementAsync(string metric, CancellationToken cancellationToken)
    {
        // Ngày lấy ở đây để bên gọi không truyền nhầm ngày. Bộ đếm đổi ngày lúc nửa đêm giờ Việt Nam, không phải giờ UTC.
        var today = VietnamCalendar.ToVietnamDate(timeProvider.GetUtcNow().UtcDateTime);

        // Một câu upsert để DB tự khóa dòng. Đọc rồi ghi bằng EF sẽ làm mất lượt khi hai request chạy cùng lúc.
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO daily_counters (date, metric, value)
            VALUES ({today}, {metric}, 1)
            ON CONFLICT (date, metric) DO UPDATE SET value = daily_counters.value + 1
            """,
            cancellationToken);
    }
}
