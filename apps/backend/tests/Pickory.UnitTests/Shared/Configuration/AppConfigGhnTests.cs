using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// SHIP-02: biến `GHN_*` có giá trị mặc định cho dev và test. Giá trị sai định dạng làm API dừng khi khởi động.
public sealed class AppConfigGhnTests
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
    // Dev và test không đặt biến nào vẫn chạy. Địa chỉ mặc định trỏ tới sandbox của GHN, không phải production.
    public void Load_WithoutGhnVariables_UsesSandboxDefaults()
    {
        var config = Load();

        Assert.Equal("https://dev-online-gateway.ghn.vn", config.GhnApiBase);
        Assert.Equal(string.Empty, config.GhnToken);
        Assert.Equal(string.Empty, config.GhnShopId);
        Assert.Equal(string.Empty, config.GhnWebhookSecret);
        Assert.False(config.GhnLiveTests);
    }

    [Fact]
    public void Load_WithGhnVariables_ReadsTrimmedValues()
    {
        var config = Load(
            ("GHN_API_BASE", " https://online-gateway.ghn.vn "),
            ("GHN_TOKEN", " token-bi-mat "),
            ("GHN_SHOP_ID", " 123456 "),
            ("GHN_WEBHOOK_SECRET", " webhook-bi-mat "),
            ("GHN_LIVE_TESTS", "1"));

        Assert.Equal("https://online-gateway.ghn.vn", config.GhnApiBase);
        Assert.Equal("token-bi-mat", config.GhnToken);
        Assert.Equal("123456", config.GhnShopId);
        Assert.Equal("webhook-bi-mat", config.GhnWebhookSecret);
        Assert.True(config.GhnLiveTests);
    }

    [Theory]
    [InlineData("không-phải-url")]
    [InlineData("ftp://online-gateway.ghn.vn")]
    // Token GHN chỉ được gửi tới địa chỉ http hoặc https hợp lệ. Giá trị khác làm API dừng khi khởi động.
    public void Load_WithInvalidGhnApiBase_Throws(string apiBase)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load(("GHN_API_BASE", apiBase)));

        Assert.Contains("GHN_API_BASE", exception.Message);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("", false)]
    public void Load_ReadsGhnLiveTestsFlag(string rawValue, bool expected)
    {
        Assert.Equal(expected, Load(("GHN_LIVE_TESTS", rawValue)).GhnLiveTests);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("2")]
    // Chỉ nhận `0` hoặc `1`. Giá trị như `true` bị từ chối để không bật test gọi GHN thật do gõ nhầm.
    public void Load_WithInvalidGhnLiveTests_Throws(string rawValue)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load(("GHN_LIVE_TESTS", rawValue)));

        Assert.Contains("GHN_LIVE_TESTS", exception.Message);
    }
}
