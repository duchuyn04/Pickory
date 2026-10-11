using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

// Cả Contracts và Entities có kiểu `Shipment`. Test này làm việc với kiểu của bên gọi.
using Shipment = Pickory.Modules.Shipping.Contracts.Shipments.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-07 lát 3: hai request tạo cùng `Ref` chạy cùng lúc chỉ để lại một vận đơn và một sự kiện.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShipmentConcurrencyTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const int ConcurrentCallers = 2;

    private readonly RecordingEventDispatcher _events = new();

    public async Task InitializeAsync()
    {
        await ShipmentTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
    }

    public Task DisposeAsync() => ShipmentTestSetup.ResetAsync(factory);

    // Mỗi bên gọi có scope và `DbContext` riêng, giống hai request HTTP thật.
    private async Task<Shipment> CreateInOwnScopeAsync(IGhnClient ghn)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = QuoteTestSetup.CreateService(scope.ServiceProvider, ghn, new ListLogger<ShippingService>(), _events);
        return await service.CreateShipmentAsync(ShipmentTestSetup.SampleRequest, CancellationToken.None);
    }

    [Fact]
    // Cổng `GatedGhnClient` giữ cả hai bên sau bước kiểm `ref`. Cả hai cùng thấy chưa có vận đơn rồi mới chèn.
    public async Task Create_TwoCallersSameRef_StoresOneShipmentOneEventAndReturnsSameShipment()
    {
        var fake = new FakeGhnClient();
        var gated = new GatedGhnClient(fake, ConcurrentCallers);

        var results = await Task.WhenAll(
            Enumerable.Range(0, ConcurrentCallers).Select(_ => Task.Run(() => CreateInOwnScopeAsync(gated))));

        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(results[0].TrackingCode, results[1].TrackingCode);
        Assert.Equal(1, fake.CreatedOrderCount);
        Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Single(await ShipmentTestSetup.ReadEventsAsync(factory));
        var published = Assert.IsType<ShipmentStatusChangedEvent>(Assert.Single(_events.Published));
        Assert.Equal(results[0].Id, published.ShipmentId);
    }
}
