using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Infrastructure;

namespace Pickory.IntegrationTests.ActivityTracking;

// Bộ đếm dựng sẵn cho test phễu. Các ngày nằm trong năm 2031 nên kết quả không đổi theo ngày chạy.
internal static class FunnelSeedData
{
    // Khoảng 2031-05-10 đến 2031-05-12 cho ra 200, 50, 10. Hai ngày 05-09 và 05-13 nằm ngoài khoảng và mỗi chỉ số 1000.
    public static readonly DateOnly OutsideBefore = new(2031, 5, 9);
    public static readonly DateOnly RangeStart = new(2031, 5, 10);
    public static readonly DateOnly RangeMiddle = new(2031, 5, 11);
    public static readonly DateOnly RangeEnd = new(2031, 5, 12);
    public static readonly DateOnly OutsideAfter = new(2031, 5, 13);

    public static async Task ResetAsync(IntegrationDatabaseFixture database)
    {
        // Dải ngày được xóa trước khi ghi, để lần chạy lại trên DB đã dùng vẫn bắt đầu từ bộ dữ liệu đúng.
        await ClearAsync(database);

        await using var context = database.CreateActivityTrackingContext();
        // Ngày `RangeEnd` chỉ có hai dòng bằng 0 và không có dòng `order_placed`, để kiểm ngày thiếu dòng tính là 0.
        (DateOnly Day, string Metric, long Value)[] rows =
        [
            (OutsideBefore, "product_view", 1000),
            (OutsideBefore, "add_to_cart", 1000),
            (OutsideBefore, "order_placed", 1000),
            (RangeStart, "product_view", 120),
            (RangeStart, "add_to_cart", 30),
            (RangeStart, "order_placed", 6),
            (RangeMiddle, "product_view", 80),
            (RangeMiddle, "add_to_cart", 20),
            (RangeMiddle, "order_placed", 4),
            (RangeEnd, "product_view", 0),
            (RangeEnd, "add_to_cart", 0),
            (OutsideAfter, "product_view", 1000),
            (OutsideAfter, "add_to_cart", 1000),
            (OutsideAfter, "order_placed", 1000),
        ];

        foreach (var (day, metric, value) in rows)
        {
            await context.Database.ExecuteSqlAsync(
                $"INSERT INTO daily_counters (date, metric, value) VALUES ({day}, {metric}, {value})");
        }
    }

    public static async Task ClearAsync(IntegrationDatabaseFixture database)
    {
        await using var context = database.CreateActivityTrackingContext();
        await context.Database.ExecuteSqlAsync(
            $"DELETE FROM daily_counters WHERE date >= {OutsideBefore} AND date <= {OutsideAfter}");
    }
}
