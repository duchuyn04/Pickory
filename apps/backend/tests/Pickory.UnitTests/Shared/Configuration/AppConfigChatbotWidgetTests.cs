using Microsoft.Extensions.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Shared.Configuration;

// CBW-01: hai địa chỉ chatbot đi theo cặp. Thiếu một địa chỉ thì khung chat không gọi được dịch vụ, nên phải dừng khi khởi động.
public sealed class AppConfigChatbotWidgetTests
{
    private const string ScriptUrlKey = "CHATBOT_WIDGET_SCRIPT_URL";
    private const string ApiUrlKey = "CHATBOT_API_URL";

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
    // Thiếu cả hai biến thì Pickory không hiện khung chat và mọi trang chạy như cũ.
    public void Load_WithoutChatbotVariables_ReportsChatbotWidgetDisabled()
    {
        var config = Load();

        Assert.False(config.HasChatbotWidget);
        Assert.Equal(string.Empty, config.ChatbotWidgetScriptUrl);
        Assert.Equal(string.Empty, config.ChatbotApiUrl);
    }

    [Fact]
    // Giá trị chỉ có khoảng trắng đếm là chưa đặt, vì file `.env` hay để lại khoảng trắng sau dấu bằng.
    public void Load_WithBlankChatbotVariables_ReportsChatbotWidgetDisabled()
    {
        var config = Load((ScriptUrlKey, "   "), (ApiUrlKey, ""));

        Assert.False(config.HasChatbotWidget);
    }

    [Fact]
    public void Load_WithBothChatbotVariables_ReadsTrimmedValues()
    {
        var config = Load(
            (ScriptUrlKey, " https://chatbot.example.com/widget.js "),
            (ApiUrlKey, "https://chatbot.example.com"));

        Assert.True(config.HasChatbotWidget);
        Assert.Equal("https://chatbot.example.com/widget.js", config.ChatbotWidgetScriptUrl);
        Assert.Equal("https://chatbot.example.com", config.ChatbotApiUrl);
    }

    [Fact]
    // Thông báo phải nêu tên biến còn thiếu để người triển khai biết cần đặt biến nào.
    public void Load_WithOnlyApiUrl_ThrowsAndNamesTheMissingScriptVariable()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Load((ApiUrlKey, "https://chatbot.example.com")));

        Assert.Contains(ScriptUrlKey, exception.Message);
    }

    [Fact]
    public void Load_WithOnlyScriptUrl_ThrowsAndNamesTheMissingApiVariable()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Load((ScriptUrlKey, "https://chatbot.example.com/widget.js")));

        Assert.Contains(ApiUrlKey, exception.Message);
    }

    [Theory]
    [InlineData("chatbot.example.com/widget.js")]
    [InlineData("/widget.js")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://chatbot.example.com/widget.js")]
    // Địa chỉ script đi thẳng vào thẻ `script` của trang khách, nên chỉ nhận URL tuyệt đối http hoặc https.
    public void Load_WithInvalidScriptUrl_Throws(string scriptUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Load((ScriptUrlKey, scriptUrl), (ApiUrlKey, "https://chatbot.example.com")));

        Assert.Contains(ScriptUrlKey, exception.Message);
    }

    [Theory]
    [InlineData("chatbot.example.com")]
    [InlineData("javascript:alert(1)")]
    public void Load_WithInvalidApiUrl_Throws(string apiUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Load((ScriptUrlKey, "https://chatbot.example.com/widget.js"), (ApiUrlKey, apiUrl)));

        Assert.Contains(ApiUrlKey, exception.Message);
    }
}
