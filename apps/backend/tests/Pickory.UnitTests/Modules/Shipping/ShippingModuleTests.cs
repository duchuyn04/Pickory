using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Addresses;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Modules.Shipping.Sync;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping;

// SHIP-02: `AddShippingModule` là điểm đăng ký duy nhất, và là nơi luật dừng ở production có hiệu lực.
public sealed class ShippingModuleTests
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    [Fact]
    // Luật dừng phải chạy ngay trong lúc đăng ký module, trước khi ứng dụng nhận request.
    public void AddShippingModule_Production_MissingGhnVariables_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddShippingModule(ConnectionString, new AppConfig(), isProduction: true));

        Assert.Contains("GHN_WEBHOOK_SECRET", exception.Message);
    }

    [Fact]
    public void AddShippingModule_Development_MissingGhnVariables_RegistersDbContext()
    {
        var services = new ServiceCollection();

        services.AddShippingModule(ConnectionString, new AppConfig(), isProduction: false);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ShippingDbContext));
    }

    [Fact]
    // SHIP-08: API phải chạy worker tra lại trạng thái vận đơn. Thiếu đăng ký này thì webhook bị mất không được bù.
    public void AddShippingModule_RegistersShipmentStatusSyncWorkerAndProcessor()
    {
        var services = new ServiceCollection();

        services.AddShippingModule(ConnectionString, new AppConfig(), isProduction: false);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(ShipmentStatusSyncWorker));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IShipmentStatusSyncProcessor)
            && descriptor.ImplementationType == typeof(ShipmentStatusSyncProcessor));
    }

    [Fact]
    // SHIP-04: API phải chạy worker đồng bộ địa chỉ. Thiếu đăng ký này thì danh mục chỉ đồng bộ khi ai đó chạy lệnh bằng tay.
    public void AddShippingModule_RegistersAddressSyncWorkerAndProcessor()
    {
        var services = new ServiceCollection();

        services.AddShippingModule(ConnectionString, new AppConfig(), isProduction: false);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(AddressSyncWorker));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAddressSyncProcessor) && descriptor.ImplementationType == typeof(AddressSyncProcessor));
    }
}
