using Pickory.Shared.Calendar;
using Xunit;

namespace Pickory.UnitTests.Shared.Calendar;

// Ngày lọc của back-office tính theo giờ Việt Nam (UTC+7, không có giờ mùa hè).
public sealed class VietnamCalendarTests
{
    [Fact]
    // Đầu ngày 5/10 giờ Việt Nam là 17:00 UTC ngày 4/10.
    public void DayStartUtc_IsVietnamMidnightExpressedInUtc()
    {
        var start = VietnamCalendar.DayStartUtc(new DateOnly(2026, 10, 5));

        Assert.Equal(new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc), start);
    }

    [Fact]
    // Cuối ngày 5/10 giờ Việt Nam là hết 23:59:59.999, tức ngay trước đầu ngày 6/10 (17:00 UTC ngày 5/10).
    public void DayEndExclusiveUtc_IsStartOfNextVietnamDay()
    {
        var endExclusive = VietnamCalendar.DayEndExclusiveUtc(new DateOnly(2026, 10, 5));

        Assert.Equal(new DateTime(2026, 10, 5, 17, 0, 0, DateTimeKind.Utc), endExclusive);
    }

    [Fact]
    // Ngày đầu tháng: 1/11 giờ Việt Nam bắt đầu lúc 17:00 UTC ngày 31/10.
    public void DayStartUtc_FirstDayOfMonth()
    {
        var start = VietnamCalendar.DayStartUtc(new DateOnly(2026, 11, 1));

        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), start);
    }

    [Fact]
    // Ngày cuối tháng kết thúc đúng lúc ngày đầu tháng sau bắt đầu, nên không có khe hở giữa hai tháng.
    public void DayEndExclusiveUtc_LastDayOfMonthMeetsNextMonthStart()
    {
        var endOfOctober = VietnamCalendar.DayEndExclusiveUtc(new DateOnly(2026, 10, 31));
        var startOfNovember = VietnamCalendar.DayStartUtc(new DateOnly(2026, 11, 1));

        Assert.Equal(startOfNovember, endOfOctober);
    }

    [Fact]
    // Đơn tạo lúc 23:30 giờ Việt Nam (16:30 UTC) nằm trong ngày đó. Đơn lúc 00:30 giờ Việt Nam (17:30 UTC hôm trước) thuộc ngày sau.
    public void DayWindow_PlacesOrdersByVietnamClock()
    {
        var day = new DateOnly(2026, 10, 5);
        var start = VietnamCalendar.DayStartUtc(day);
        var endExclusive = VietnamCalendar.DayEndExclusiveUtc(day);
        var at2330 = new DateTime(2026, 10, 5, 16, 30, 0, DateTimeKind.Utc);
        var at0030NextDay = new DateTime(2026, 10, 5, 17, 30, 0, DateTimeKind.Utc);

        Assert.True(at2330 >= start && at2330 < endExclusive);
        Assert.False(at0030NextDay >= start && at0030NextDay < endExclusive);
    }

    [Fact]
    // Giây cuối cùng trước nửa đêm giờ Việt Nam (16:59:59.999999 UTC) vẫn thuộc ngày D.
    public void ToVietnamDate_LastMomentBeforeVietnamMidnight_IsSameDay()
    {
        var moment = new DateTime(2026, 10, 5, 16, 59, 59, DateTimeKind.Utc).AddTicks(9_999);

        Assert.Equal(new DateOnly(2026, 10, 5), VietnamCalendar.ToVietnamDate(moment));
    }

    [Fact]
    // Đúng 17:00:00 UTC là nửa đêm giờ Việt Nam, nên thuộc ngày D+1.
    public void ToVietnamDate_VietnamMidnight_IsNextDay()
    {
        var moment = new DateTime(2026, 10, 5, 17, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 10, 6), VietnamCalendar.ToVietnamDate(moment));
    }

    [Fact]
    // Bên gọi có thể truyền `DateTime` có `Kind` không xác định. Hàm vẫn phải hiểu giá trị đó là giờ UTC.
    public void ToVietnamDate_UnspecifiedKind_IsTreatedAsUtc()
    {
        var moment = new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(new DateOnly(2027, 1, 1), VietnamCalendar.ToVietnamDate(moment));
    }

    [Fact]
    // Ngày cuối năm vẫn cộng đúng một ngày.
    public void DayEndExclusiveUtc_HandlesYearBoundary()
    {
        var endExclusive = VietnamCalendar.DayEndExclusiveUtc(new DateOnly(2026, 12, 31));

        Assert.Equal(new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc), endExclusive);
    }

    [Fact]
    // 16:59 UTC là 23:59 cùng ngày, nên giờ treo tường chưa sang ngày mới.
    public void ToVietnamDateTime_BeforeVietnamMidnight_KeepsSameDay()
    {
        var moment = new DateTime(2026, 11, 9, 16, 59, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 11, 9, 23, 59, 0), VietnamCalendar.ToVietnamDateTime(moment));
    }

    [Fact]
    // 17:00 UTC là nửa đêm giờ Việt Nam, nên giờ treo tường sang ngày kế tiếp.
    public void ToVietnamDateTime_AtVietnamMidnight_MovesToNextDay()
    {
        var moment = new DateTime(2026, 11, 9, 17, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 11, 10, 0, 0, 0), VietnamCalendar.ToVietnamDateTime(moment));
    }

    [Fact]
    // `Kind` không xác định vẫn được hiểu là UTC, và kết quả không mang múi giờ nào.
    public void ToVietnamDateTime_UnspecifiedKind_IsTreatedAsUtc_AndResultIsUnspecified()
    {
        var moment = new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Unspecified);

        var result = VietnamCalendar.ToVietnamDateTime(moment);

        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0), result);
        Assert.Equal(DateTimeKind.Unspecified, result.Kind);
    }
}
