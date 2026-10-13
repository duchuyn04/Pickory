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
    // Ngày cuối năm vẫn cộng đúng một ngày.
    public void DayEndExclusiveUtc_HandlesYearBoundary()
    {
        var endExclusive = VietnamCalendar.DayEndExclusiveUtc(new DateOnly(2026, 12, 31));

        Assert.Equal(new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc), endExclusive);
    }
}
