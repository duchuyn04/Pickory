using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.Shared.Configuration;

namespace Pickory.IntegrationTests.Infrastructure;

// Factory có hai địa chỉ chatbot giả. Test dùng nó để kiểm tra route `GET /api/chatbot-widget` khi widget bật.
public sealed class ConfiguredChatbotWebApplicationFactory(ApiDatabaseFixture database)
    : PickoryWebApplicationFactory(database)
{
    public const string FakeScriptUrl = "https://chatbot.example.test/widget.js";
    public const string FakeApiUrl = "https://chatbot.example.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Đăng ký sau lớp cha nên `AppConfig` cuối cùng mang hai địa chỉ giả.
        builder.ConfigureTestServices(services =>
        {
            var currentConfig = (AppConfig)services.Single(descriptor => descriptor.ServiceType == typeof(AppConfig)).ImplementationInstance!;
            services.RemoveAll<AppConfig>();
            services.AddSingleton(currentConfig with { ChatbotWidgetScriptUrl = FakeScriptUrl, ChatbotApiUrl = FakeApiUrl });
        });
    }
}
