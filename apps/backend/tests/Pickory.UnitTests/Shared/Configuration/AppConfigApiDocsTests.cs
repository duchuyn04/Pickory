using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// `API_DOCS_ENABLED` bật tài liệu API ở production. Tài liệu liệt kê cả route admin, nên mặc định tắt.
public sealed class AppConfigApiDocsTests
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
    // Không đặt biến thì tài liệu tắt. Bản triển khai cũ không tự lộ danh sách route sau khi nâng cấp.
    public void Load_WithoutApiDocsVariable_DisablesDocs()
    {
        Assert.False(Load().ApiDocsEnabled);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("", false)]
    public void Load_ReadsApiDocsFlag(string rawValue, bool expected)
    {
        Assert.Equal(expected, Load(("API_DOCS_ENABLED", rawValue)).ApiDocsEnabled);
    }

    [Fact]
    // Không đặt biến thì tài liệu production chỉ xem. Bản triển khai cũ không tự mở nút gọi thử.
    public void Load_WithoutInteractiveVariable_DisablesInteractive()
    {
        Assert.False(Load().ApiDocsInteractive);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("", false)]
    public void Load_ReadsApiDocsInteractiveFlag(string rawValue, bool expected)
    {
        Assert.Equal(expected, Load(("API_DOCS_INTERACTIVE", rawValue)).ApiDocsInteractive);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("2")]
    // Chỉ nhận `0` hoặc `1`, giống `GHN_LIVE_TESTS`. Giá trị sai làm API dừng khi khởi động, thay vì bật hoặc tắt tài liệu ngoài ý muốn.
    public void Load_WithInvalidApiDocsFlag_Throws(string rawValue)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load(("API_DOCS_ENABLED", rawValue)));

        Assert.Contains("API_DOCS_ENABLED", exception.Message);
    }
}
