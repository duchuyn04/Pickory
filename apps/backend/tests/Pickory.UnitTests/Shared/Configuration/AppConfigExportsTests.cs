using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exports;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// `Exports:MaxRows` đổi giới hạn dòng của file xuất. `AppConfig.Load` phải mang giá trị này tới các route.
public sealed class AppConfigExportsTests
{
    private static AppConfig Load(string? maxRows)
    {
        var values = new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
            ["Exports:MaxRows"] = maxRows,
        };

        return AppConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Fact]
    // Không đặt khóa thì giới hạn là 50.000 của spec.
    public void Load_WithoutMaxRows_UsesSpecLimit()
    {
        Assert.Equal(ExportLimits.MaxRows, Load(null).Exports.MaxRows);
    }

    [Fact]
    // Khóa hợp lệ đi từ cấu hình tới `AppConfig.Exports`.
    public void Load_WithMaxRows_CarriesValue()
    {
        Assert.Equal(3, Load("3").Exports.MaxRows);
    }

    [Fact]
    // Khóa sai làm `AppConfig.Load` ném lỗi, nên API dừng khi khởi động.
    public void Load_WithInvalidMaxRows_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Load("0"));
    }
}
