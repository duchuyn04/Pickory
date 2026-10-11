using Microsoft.Extensions.DependencyInjection;
using Pickory.Modules.Commerce;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce;

public sealed class CommerceModuleTests
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    [Fact]
    // Luật dừng phải chạy ngay trong lúc đăng ký module, trước khi ứng dụng nhận request.
    public void AddCommerceModule_Production_MissingSepayKey_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddCommerceModule(ConnectionString, new AppConfig(), isProduction: true));

        Assert.Contains("SEPAY_WEBHOOK_API_KEY", exception.Message);
    }

    [Fact]
    public void AddCommerceModule_Development_MissingSepayKey_RegistersDbContext()
    {
        var services = new ServiceCollection();

        services.AddCommerceModule(ConnectionString, new AppConfig(), isProduction: false);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(CommerceDbContext));
    }
}
