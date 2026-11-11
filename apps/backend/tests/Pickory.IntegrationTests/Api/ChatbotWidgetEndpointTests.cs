using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.IntegrationTests.Api;

// CBW-01: route công khai cho frontend biết có hiện khung chat hay không.
[Collection(ApiCollection.Name)]
public sealed class ChatbotWidgetEndpointTests(ApiDatabaseFixture database)
{
    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        // Clone để phần tử còn dùng được sau khi `document` được giải phóng.
        return document.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    // Thiếu cả hai biến thì route vẫn trả 200. Frontend đọc `enabled: false` và không chèn script.
    public async Task GetChatbotWidget_WithoutConfiguration_ReturnsDisabledWithNullAddresses()
    {
        using var factory = new PickoryWebApplicationFactory(database);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/chatbot-widget");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        Assert.False(data.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("scriptUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("apiUrl").ValueKind);
    }

    [Fact]
    public async Task GetChatbotWidget_WithConfiguration_ReturnsConfiguredAddresses()
    {
        using var factory = new ConfiguredChatbotWebApplicationFactory(database);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/chatbot-widget");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync(response);
        Assert.True(data.GetProperty("enabled").GetBoolean());
        Assert.Equal(ConfiguredChatbotWebApplicationFactory.FakeScriptUrl, data.GetProperty("scriptUrl").GetString());
        Assert.Equal(ConfiguredChatbotWebApplicationFactory.FakeApiUrl, data.GetProperty("apiUrl").GetString());
    }

    [Fact]
    // Máy chạy test có thể đặt biến chatbot thật. Factory mặc định phải bỏ qua chúng, để test khác không thấy widget bật.
    public void Factory_IgnoresChatbotEnvironmentVariablesOfTheTestMachine()
    {
        // Đồng bộ hóa để không gây race condition biến môi trường với các collection song song khác.
        lock (ProcessEnvironmentLock.Gate)
        {
            // Lưu giá trị cũ để trả lại máy chạy test như ban đầu, kể cả khi assertion lỗi.
            var originalScriptUrl = Environment.GetEnvironmentVariable("CHATBOT_WIDGET_SCRIPT_URL");
            var originalApiUrl = Environment.GetEnvironmentVariable("CHATBOT_API_URL");
            try
            {
                Environment.SetEnvironmentVariable("CHATBOT_WIDGET_SCRIPT_URL", "https://may-test.example/widget.js");
                Environment.SetEnvironmentVariable("CHATBOT_API_URL", "https://may-test.example");

                using var factory = new PickoryWebApplicationFactory(database);
                var config = factory.Services.GetRequiredService<AppConfig>();

                Assert.False(config.HasChatbotWidget);
            }
            finally
            {
                Environment.SetEnvironmentVariable("CHATBOT_WIDGET_SCRIPT_URL", originalScriptUrl);
                Environment.SetEnvironmentVariable("CHATBOT_API_URL", originalApiUrl);
            }
        }
    }
}
