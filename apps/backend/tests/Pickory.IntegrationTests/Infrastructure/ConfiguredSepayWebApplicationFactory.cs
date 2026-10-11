using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.Shared.Configuration;

namespace Pickory.IntegrationTests.Infrastructure;

// Factory có khóa webhook SePay giả. Test dùng nó để kiểm tra API và audit không lộ khóa này.
public sealed class ConfiguredSepayWebApplicationFactory(IntegrationDatabaseFixture database)
    : PickoryWebApplicationFactory(database)
{
    // Chuỗi dễ nhận ra trong thân response và trong dòng audit.
    public const string FakeSepayWebhookApiKey = "sepay-khoa-bi-mat-de-nhan-ra-0003";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Đăng ký sau lớp cha nên `AppConfig` cuối cùng mang khóa giả.
        builder.ConfigureTestServices(services =>
        {
            var currentConfig = (AppConfig)services.Single(descriptor => descriptor.ServiceType == typeof(AppConfig)).ImplementationInstance!;
            services.RemoveAll<AppConfig>();
            services.AddSingleton(currentConfig with { SepayWebhookApiKey = FakeSepayWebhookApiKey });
        });
    }
}
