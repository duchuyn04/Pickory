using Pickory.Api.ChatbotWidget;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Api;

// CBW-01: ngoài Development, địa chỉ chatbot phải dùng `https`. Trang khách `https` chặn script `http`.
public sealed class ChatbotWidgetConfigurationCheckTests
{
    private static AppConfig CreateConfig(string scriptUrl, string apiUrl)
    {
        return new AppConfig { ChatbotWidgetScriptUrl = scriptUrl, ChatbotApiUrl = apiUrl };
    }

    [Fact]
    // Máy dev chạy `chatbot-rag` ở `http://localhost`.
    public void Development_AllowsHttpAddresses()
    {
        var config = CreateConfig("http://localhost:8000/widget.js", "http://localhost:8000");

        var exception = Record.Exception(() => ChatbotWidgetConfigurationCheck.EnsureSecureOutsideDevelopment(config, isDevelopment: true));

        Assert.Null(exception);
    }

    [Fact]
    public void OutsideDevelopment_AllowsHttpsAddresses()
    {
        var config = CreateConfig("https://chatbot.example.com/widget.js", "https://chatbot.example.com");

        var exception = Record.Exception(() => ChatbotWidgetConfigurationCheck.EnsureSecureOutsideDevelopment(config, isDevelopment: false));

        Assert.Null(exception);
    }

    [Fact]
    // Không cấu hình chatbot là trạng thái hợp lệ ở mọi môi trường.
    public void OutsideDevelopment_WithoutChatbotConfiguration_DoesNotThrow()
    {
        var exception = Record.Exception(() => ChatbotWidgetConfigurationCheck.EnsureSecureOutsideDevelopment(new AppConfig(), isDevelopment: false));

        Assert.Null(exception);
    }

    [Fact]
    public void OutsideDevelopment_WithHttpScriptUrl_ThrowsAndNamesTheVariable()
    {
        var config = CreateConfig("http://chatbot.example.com/widget.js", "https://chatbot.example.com");

        var exception = Assert.Throws<InvalidOperationException>(
            () => ChatbotWidgetConfigurationCheck.EnsureSecureOutsideDevelopment(config, isDevelopment: false));

        Assert.Contains("CHATBOT_WIDGET_SCRIPT_URL", exception.Message);
    }

    [Fact]
    public void OutsideDevelopment_WithHttpApiUrl_ThrowsAndNamesTheVariable()
    {
        var config = CreateConfig("https://chatbot.example.com/widget.js", "http://chatbot.example.com");

        var exception = Assert.Throws<InvalidOperationException>(
            () => ChatbotWidgetConfigurationCheck.EnsureSecureOutsideDevelopment(config, isDevelopment: false));

        Assert.Contains("CHATBOT_API_URL", exception.Message);
    }
}
