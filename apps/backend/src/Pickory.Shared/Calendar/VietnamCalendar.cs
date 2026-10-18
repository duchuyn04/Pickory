using System.Globalization;

namespace Pickory.Shared.Calendar;

// Ngày và tháng theo giờ Việt Nam. Ngân sách tháng, hạn mức ngày và bộ lọc ngày của back-office đổi lúc nửa đêm giờ Việt Nam.
public static class VietnamCalendar
{
    // Việt Nam dùng UTC+7 cố định và không có giờ mùa hè.
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static MonthWindow MonthWindow(DateTimeOffset now)
    {
        var vietnamTime = now.ToOffset(VietnamOffset);
        var startOfMonth = new DateTimeOffset(vietnamTime.Year, vietnamTime.Month, 1, 0, 0, 0, VietnamOffset);
        return new MonthWindow(startOfMonth.UtcDateTime, startOfMonth.AddMonths(1).UtcDateTime);
    }

    // Đầu ngày giờ Việt Nam, đổi sang UTC. Dùng làm cận dưới (gồm cận) khi lọc theo ngày.
    public static DateTime DayStartUtc(DateOnly day)
    {
        // Nửa đêm giờ Việt Nam là 17:00 UTC của ngày hôm trước. Dùng offset cố định để không phụ thuộc múi giờ của máy chủ.
        var startOfDay = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, VietnamOffset);
        return startOfDay.UtcDateTime;
    }

    // Đầu ngày kế tiếp giờ Việt Nam, đổi sang UTC. Dùng làm cận trên (không gồm cận) để ngày được tính hết 23:59:59.999.
    public static DateTime DayEndExclusiveUtc(DateOnly day)
    {
        // Cận không gồm tránh làm tròn mili giây và không để lọt một khoảnh khắc nào giữa hai ngày liền nhau.
        return DayStartUtc(day.AddDays(1));
    }

    // Ngày lịch giờ Việt Nam của một thời điểm UTC. Báo cáo dùng hàm này để xếp đơn và giao dịch vào đúng ngày.
    public static DateOnly ToVietnamDate(DateTime utc)
    {
        // Bên gọi có thể truyền `DateTime` có `Kind` không xác định. Gán `Utc` để phép đổi offset không dùng múi giờ của máy chủ.
        var utcMoment = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        var vietnamMoment = utcMoment.ToOffset(VietnamOffset);
        return DateOnly.FromDateTime(vietnamMoment.DateTime);
    }

    // Trả dạng `yyyy-MM-dd`. Dạng này sắp xếp đúng theo thứ tự thời gian khi so chuỗi.
    public static string DayKey(DateTimeOffset moment)
    {
        return moment.ToOffset(VietnamOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
