using Pickory.Shared.Persistence;
using Xunit;

namespace Pickory.UnitTests.Shared.Persistence;

public class UtcTimestampConverterTests
{
    [Fact]
    public void ReadAndWrite_PreserveUtcTicksWithoutTimezoneShift()
    {
        var converter = new UtcTimestampConverter();
        var databaseValue = new DateTime(2026, 10, 5, 7, 8, 9, 123, DateTimeKind.Unspecified);

        // Kiểm tra cả hai chiều để phát hiện việc áp múi giờ máy chủ vào giá trị DB.
        var applicationValue = Assert.IsType<DateTime>(converter.ConvertFromProvider(databaseValue));
        var writtenValue = Assert.IsType<DateTime>(converter.ConvertToProvider(applicationValue));

        Assert.Equal(DateTimeKind.Utc, applicationValue.Kind);
        Assert.Equal(databaseValue.Ticks, applicationValue.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, writtenValue.Kind);
        Assert.Equal(databaseValue.Ticks, writtenValue.Ticks);
    }

    [Fact]
    public void Write_LocalValuePreservesInstantAsUtc()
    {
        var converter = new UtcTimestampConverter();
        var utcValue = new DateTime(2026, 10, 5, 7, 8, 9, 123, DateTimeKind.Utc);

        // Giờ địa phương phải ghi cùng thời điểm UTC (Coordinated Universal Time), không ghi nguyên giờ hiển thị địa phương.
        var writtenValue = Assert.IsType<DateTime>(converter.ConvertToProvider(utcValue.ToLocalTime()));

        Assert.Equal(utcValue.Ticks, writtenValue.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, writtenValue.Kind);
    }
}
