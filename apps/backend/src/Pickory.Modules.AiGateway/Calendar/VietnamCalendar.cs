using System.Globalization;

namespace Pickory.Modules.AiGateway.Calendar;

// Ngày và tháng theo giờ Việt Nam. Ngân sách tháng và hạn mức ngày đổi lúc nửa đêm giờ Việt Nam.
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

    // Trả dạng `yyyy-MM-dd`. Dạng này sắp xếp đúng theo thứ tự thời gian khi so chuỗi.
    public static string DayKey(DateTimeOffset moment)
    {
        return moment.ToOffset(VietnamOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
