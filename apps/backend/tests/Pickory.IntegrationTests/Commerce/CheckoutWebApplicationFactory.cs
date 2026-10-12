using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Shipping.Ghn;

namespace Pickory.IntegrationTests.Commerce;

// Factory của test checkout. Client GHN được bọc để test đọc kiện hàng đã gửi, và log được giữ lại để kiểm log lỗi.
public sealed class CheckoutWebApplicationFactory(IntegrationDatabaseFixture database)
    : PickoryWebApplicationFactory(database)
{
    public CapturingLoggerProvider Logs { get; } = new();

    // Bản bọc của `FakeGhnClient`. Chế độ lỗi vẫn đặt qua `GhnClient` của lớp cha.
    internal RecordingGhnClient Recorder => Services.GetRequiredService<RecordingGhnClient>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        // Đăng ký sau lớp cha nên bản bọc thay bản giả trần. Cả hai trỏ tới cùng một `FakeGhnClient`.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGhnClient>();
            services.AddSingleton(provider => new RecordingGhnClient(provider.GetRequiredService<FakeGhnClient>()));
            services.AddSingleton<IGhnClient>(provider => provider.GetRequiredService<RecordingGhnClient>());
        });
    }
}
