using Microsoft.Extensions.Configuration;
using Pickory.Shared.Exports;
using Xunit;

namespace Pickory.UnitTests.Shared.Exports;

public sealed class ExportOptionsTests
{
    private static IConfiguration Configure(string? maxRows)
    {
        var values = new Dictionary<string, string?>();
        if (maxRows is not null)
        {
            values["Exports:MaxRows"] = maxRows;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // Không đặt khóa thì dùng giới hạn 50.000 của spec.
    public void Load_WithoutValue_UsesDefaultLimit(string? rawValue)
    {
        var options = ExportOptions.Load(Configure(rawValue));

        Assert.Equal(ExportLimits.MaxRows, options.MaxRows);
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData(" 7 ", 7)]
    [InlineData("1", 1)]
    [InlineData("2147483646", 2_147_483_646)]
    // Giá trị hợp lệ thay giới hạn mặc định, kể cả cận dưới 1 và cận trên cho phép cộng một.
    public void Load_WithValidValue_UsesThatValue(string rawValue, int expected)
    {
        var options = ExportOptions.Load(Configure(rawValue));

        Assert.Equal(expected, options.MaxRows);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("2147483647")]
    [InlineData("99999999999")]
    // Giá trị sai làm khởi động lỗi và thông báo nêu tên khóa, không im lặng dùng mặc định.
    public void Load_WithInvalidValue_ThrowsWithKeyName(string rawValue)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ExportOptions.Load(Configure(rawValue)));

        Assert.Contains("Exports:MaxRows", exception.Message, StringComparison.Ordinal);
        // Thông báo không in lại giá trị đã nhập.
        Assert.DoesNotContain(rawValue, exception.Message, StringComparison.Ordinal);
    }
}
