using Microsoft.Extensions.Time.Testing;
using Pickory.Modules.SupportDesk.Scheduling;
using Xunit;

namespace Pickory.UnitTests.Modules.SupportDesk.Scheduling;

// SD-01: hàm thuần kiểm thời điểm UTC có nằm trong giờ làm việc giờ Việt Nam hay không.
public sealed class WorkingHoursCalendarTests
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private static readonly WorkingHoursSettings DefaultSettings = new(
        OpensAt: new TimeOnly(8, 0),
        ClosesAt: new TimeOnly(21, 0),
        Holidays: Array.Empty<DateOnly>());

    // Đặt đồng hồ giả ở một giờ Việt Nam cụ thể. Đọc lại bằng `GetUtcNow` để có thời điểm UTC giống đầu vào thật.
    private static DateTimeOffset UtcAtVietnamTime(int year, int month, int day, int hour, int minute)
    {
        var vietnamMoment = new DateTimeOffset(year, month, day, hour, minute, 0, VietnamOffset);
        var clock = new FakeTimeProvider(vietnamMoment.ToUniversalTime());
        return clock.GetUtcNow();
    }

    [Fact]
    public void IsWithinHours_NullSettings_ThrowsForThatParameter()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            WorkingHoursCalendar.IsWithinHours(DateTimeOffset.UnixEpoch, settings: null!));

        Assert.Equal("settings", exception.ParamName);
    }

    [Theory]
    [InlineData(7, 59, false)]
    [InlineData(8, 0, true)]
    [InlineData(12, 30, true)]
    [InlineData(20, 59, true)]
    [InlineData(21, 0, false)]
    [InlineData(23, 59, false)]
    [InlineData(0, 0, false)]
    public void IsWithinHours_UsesHalfOpenWindowInVietnamTime(int hour, int minute, bool expected)
    {
        var utcNow = UtcAtVietnamTime(2026, 10, 22, hour, minute);

        Assert.Equal(expected, WorkingHoursCalendar.IsWithinHours(utcNow, DefaultSettings));
    }

    [Fact]
    public void IsWithinHours_HolidayIsOutsideHoursAtTenInTheMorning()
    {
        var settings = DefaultSettings with { Holidays = new[] { new DateOnly(2026, 10, 22) } };
        var utcNow = UtcAtVietnamTime(2026, 10, 22, 10, 0);

        Assert.False(WorkingHoursCalendar.IsWithinHours(utcNow, settings));
    }

    [Fact]
    public void IsWithinHours_DayAfterHolidayIsStillWorkingDay()
    {
        var settings = DefaultSettings with { Holidays = new[] { new DateOnly(2026, 10, 22) } };
        var utcNow = UtcAtVietnamTime(2026, 10, 23, 10, 0);

        Assert.True(WorkingHoursCalendar.IsWithinHours(utcNow, settings));
    }

    // UTC 00:30 ngày D là 7:30 ngày D giờ Việt Nam, nên vẫn chưa mở cửa.
    [Fact]
    public void IsWithinHours_UtcHalfPastMidnight_IsSevenThirtyInVietnamAndOutsideHours()
    {
        var utcNow = new DateTimeOffset(2026, 10, 22, 0, 30, 0, TimeSpan.Zero);

        Assert.False(WorkingHoursCalendar.IsWithinHours(utcNow, DefaultSettings));
    }

    // UTC 01:00 ngày D là 8:00 ngày D giờ Việt Nam, đúng giờ mở cửa.
    [Fact]
    public void IsWithinHours_UtcOneAm_IsEightInVietnamAndWithinHours()
    {
        var utcNow = new DateTimeOffset(2026, 10, 22, 1, 0, 0, TimeSpan.Zero);

        Assert.True(WorkingHoursCalendar.IsWithinHours(utcNow, DefaultSettings));
    }

    // UTC 17:30 ngày D-1 là 0:30 ngày D giờ Việt Nam. Ngày nghỉ phải so theo ngày Việt Nam, không theo ngày UTC.
    [Fact]
    public void IsWithinHours_HolidayIsComparedByVietnamDateNotUtcDate()
    {
        var holidayInVietnam = new DateOnly(2026, 10, 22);
        var settings = DefaultSettings with
        {
            OpensAt = new TimeOnly(0, 0),
            ClosesAt = new TimeOnly(23, 0),
            Holidays = new[] { holidayInVietnam },
        };
        var utcNow = new DateTimeOffset(2026, 10, 21, 17, 30, 0, TimeSpan.Zero);

        Assert.False(WorkingHoursCalendar.IsWithinHours(utcNow, settings));
    }

    // Cùng thời điểm, nhưng ngày UTC (D-1) là ngày nghỉ còn ngày Việt Nam (D) thì không. Kết quả phải là trong giờ.
    [Fact]
    public void IsWithinHours_UtcDateIsHolidayButVietnamDateIsNot_IsWithinHours()
    {
        var settings = DefaultSettings with
        {
            OpensAt = new TimeOnly(0, 0),
            ClosesAt = new TimeOnly(23, 0),
            Holidays = new[] { new DateOnly(2026, 10, 21) },
        };
        var utcNow = new DateTimeOffset(2026, 10, 21, 17, 30, 0, TimeSpan.Zero);

        Assert.True(WorkingHoursCalendar.IsWithinHours(utcNow, settings));
    }

    // Đầu vào mang offset khác UTC vẫn phải đổi đúng. 15:00 ở UTC-5 là 03:00 giờ Việt Nam hôm sau.
    [Fact]
    public void IsWithinHours_InputWithNonUtcOffset_IsConvertedToVietnamTime()
    {
        var utcNow = new DateTimeOffset(2026, 10, 22, 15, 0, 0, TimeSpan.FromHours(-5));

        Assert.False(WorkingHoursCalendar.IsWithinHours(utcNow, DefaultSettings));
    }
}
