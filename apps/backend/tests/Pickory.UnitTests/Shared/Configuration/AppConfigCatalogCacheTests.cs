using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// `CATALOG_CACHE_SECONDS` bật cache ngắn cho danh sách sản phẩm và cây danh mục công khai. Mặc định tắt.
public sealed class AppConfigCatalogCacheTests
{
    private static AppConfig Load(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://pickory:pickory@localhost:5432/pickory",
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return AppConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Fact]
    // Không đặt biến thì cache tắt, để dev và test giữ hành vi cũ.
    public void Load_WithoutVariable_DisablesTheCache()
    {
        Assert.Equal(0, Load().CatalogCacheSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_WithEmptyVariable_DisablesTheCache(string rawValue)
    {
        Assert.Equal(0, Load(("CATALOG_CACHE_SECONDS", rawValue)).CatalogCacheSeconds);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("30", 30)]
    [InlineData("300", 300)]
    public void Load_ReadsSecondsInsideTheAllowedRange(string rawValue, int expected)
    {
        Assert.Equal(expected, Load(("CATALOG_CACHE_SECONDS", rawValue)).CatalogCacheSeconds);
    }

    [Theory]
    [InlineData("301")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    // Giá trị ngoài khoảng 0 đến 300 làm API dừng khi khởi động. Thông báo nêu tên biến.
    public void Load_WithInvalidValue_ThrowsAndNamesTheVariable(string rawValue)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load(("CATALOG_CACHE_SECONDS", rawValue)));

        Assert.Contains("CATALOG_CACHE_SECONDS", exception.Message);
    }
}
