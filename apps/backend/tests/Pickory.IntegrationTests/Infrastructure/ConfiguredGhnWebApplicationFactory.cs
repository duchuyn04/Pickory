using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.Shared.Configuration;

namespace Pickory.IntegrationTests.Infrastructure;

// Factory có token, shop id và bí mật webhook GHN giả. Test dùng nó để kiểm tra API không lộ các giá trị này.
public sealed class ConfiguredGhnWebApplicationFactory(IntegrationDatabaseFixture database) : PickoryWebApplicationFactory(database)
{
    // Giá trị dễ nhận ra trong thân response. Test khẳng định thân response không chứa hai chuỗi bí mật.
    public const string FakeGhnToken = "ghn-token-bi-mat-de-nhan-ra-0001";
    public const string FakeGhnWebhookSecret = "ghn-webhook-bi-mat-de-nhan-ra-0002";
    public const string FakeGhnShopId = "123456";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Đăng ký sau lớp cha nên chạy sau bước xóa cấu hình GHN của lớp cha. `AppConfig` cuối cùng mang giá trị giả.
        builder.ConfigureTestServices(services =>
        {
            var currentConfig = (AppConfig)services.Single(descriptor => descriptor.ServiceType == typeof(AppConfig)).ImplementationInstance!;
            services.RemoveAll<AppConfig>();
            services.AddSingleton(currentConfig with
            {
                GhnToken = FakeGhnToken,
                GhnShopId = FakeGhnShopId,
                GhnWebhookSecret = FakeGhnWebhookSecret,
            });
        });
    }
}
