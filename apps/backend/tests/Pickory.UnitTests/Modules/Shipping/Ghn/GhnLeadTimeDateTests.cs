using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// SHIP-05 lát 4: GHN trả thời gian giao dạng số giây Unix. Ngày giao tính theo giờ Việt Nam (UTC+7), không theo múi giờ máy chủ.
public sealed class GhnLeadTimeDateTests
{
    private static long UnixSeconds(int year, int month, int day, int hour, int minute, int second, int offsetHours) =>
        new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(offsetHours)).ToUnixTimeSeconds();

    [Fact]
    public void ToVietnamDate_MiddayInVietnam_ReturnsSameCalendarDate()
    {
        var seconds = UnixSeconds(2026, 10, 15, 12, 0, 0, offsetHours: 7);

        Assert.Equal(new DateOnly(2026, 10, 15), GhnLeadTimeDate.ToVietnamDate(seconds));
    }

    [Fact]
    // 16:59:59 UTC là 23:59:59 giờ Việt Nam, vẫn còn ngày 15.
    public void ToVietnamDate_OneSecondBeforeVietnamMidnight_StaysOnSameDate()
    {
        var seconds = UnixSeconds(2026, 10, 15, 16, 59, 59, offsetHours: 0);

        Assert.Equal(new DateOnly(2026, 10, 15), GhnLeadTimeDate.ToVietnamDate(seconds));
    }

    [Fact]
    // 17:00:00 UTC là 00:00:00 giờ Việt Nam của ngày 16. Tính theo UTC sẽ ra ngày 15 và sai một ngày.
    public void ToVietnamDate_ExactlyVietnamMidnight_ReturnsNextDate()
    {
        var seconds = UnixSeconds(2026, 10, 15, 17, 0, 0, offsetHours: 0);

        Assert.Equal(new DateOnly(2026, 10, 16), GhnLeadTimeDate.ToVietnamDate(seconds));
    }

    [Fact]
    public void ToVietnamDate_RollsOverMonthAndYear()
    {
        var seconds = UnixSeconds(2026, 12, 31, 18, 30, 0, offsetHours: 0);

        Assert.Equal(new DateOnly(2027, 1, 1), GhnLeadTimeDate.ToVietnamDate(seconds));
    }
}
