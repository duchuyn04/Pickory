using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Ghn;

// SHIP-03 lát 5: `AddShippingModule` chọn client GHN thật khi có token, và client giả khi thiếu.
public sealed class GhnRegistrationTests
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    private static ServiceProvider Build(AppConfig config, bool isProduction = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShippingModule(ConnectionString, config, isProduction);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void WithToken_ResolvesGhnHttpClient()
    {
        using var provider = Build(new AppConfig { GhnToken = "co-token", GhnShopId = "1", GhnApiBase = "https://ghn.test" });

        Assert.IsType<GhnHttpClient>(provider.GetRequiredService<IGhnClient>());
    }

    [Fact]
    public void WithoutToken_ResolvesFakeGhnClient()
    {
        using var provider = Build(new AppConfig());

        Assert.IsType<FakeGhnClient>(provider.GetRequiredService<IGhnClient>());
    }

    [Fact]
    // Chỉ có khoảng trắng thì coi như thiếu token. `AppConfig.Load` đã cắt khoảng trắng, nhưng `AppConfig` dựng tay thì chưa.
    public void WithBlankToken_ResolvesFakeGhnClient()
    {
        using var provider = Build(new AppConfig { GhnToken = "   " });

        Assert.IsType<FakeGhnClient>(provider.GetRequiredService<IGhnClient>());
    }

    [Fact]
    // Client giả giữ trạng thái đơn đã tạo, nên mọi nơi dùng phải thấy cùng một thể hiện.
    public void FakeClient_IsSharedAcrossScopes()
    {
        using var provider = Build(new AppConfig());

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<IGhnClient>(),
            secondScope.ServiceProvider.GetRequiredService<IGhnClient>());
    }

    [Fact]
    public void RealClient_UsesConfiguredApiBase()
    {
        // Địa chỉ gốc lấy từ `GHN_API_BASE`, nên production đổi sang cổng thật chỉ bằng cấu hình.
        using var provider = Build(new AppConfig { GhnToken = "co-token", GhnShopId = "1", GhnApiBase = "https://online-gateway.ghn.vn" });

        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(new Uri("https://online-gateway.ghn.vn"), factory.CreateClient(GhnRegistration.HttpClientName).BaseAddress);
        Assert.Equal(Timeout.InfiniteTimeSpan, factory.CreateClient(GhnRegistration.HttpClientName).Timeout);
    }
}
