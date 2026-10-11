using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

// Cả Contracts và Entities có kiểu `Shipment`. Test này làm việc với kiểu của bên gọi.
using Shipment = Pickory.Modules.Shipping.Contracts.Shipments.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-07: bên gọi hủy request sau khi GHN đã tạo hoặc hủy đơn. DB vẫn phải ghi nhận kết quả của GHN.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShipmentCallerCancellationTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly FakeGhnClient _fake = new();
    private readonly RecordingEventDispatcher _events = new();
    private readonly ListLogger<ShippingService> _logger = new();

    public async Task InitializeAsync()
    {
        await ShipmentTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
    }

    public Task DisposeAsync() => ShipmentTestSetup.ResetAsync(factory);

    [Fact]
    public async Task Create_CallerCancelsAfterGhnCreated_StillStoresShipmentAndPublishesOneEvent()
    {
        using var callerCancellation = new CancellationTokenSource();
        var ghn = new CancelAfterGhnClient(_fake, callerCancellation);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = QuoteTestSetup.CreateService(scope.ServiceProvider, ghn, _logger, _events);
            await service.CreateShipmentAsync(ShipmentTestSetup.SampleRequest, callerCancellation.Token);
        }

        Assert.True(callerCancellation.IsCancellationRequested);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal("ship07-ref-1", stored.Ref);
        Assert.Equal(ShipmentStatus.PendingPickup, stored.Status);
        Assert.Single(await ShipmentTestSetup.ReadEventsAsync(factory));
        Assert.Single(_events.Published.OfType<ShipmentStatusChangedEvent>());
    }

    [Fact]
    public async Task Cancel_CallerCancelsAfterGhnCancelled_StillStoresCancelledStatusRenamesRefAndPublishesOnce()
    {
        Shipment shipment;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = QuoteTestSetup.CreateService(scope.ServiceProvider, _fake, _logger, _events);
            shipment = await service.CreateShipmentAsync(ShipmentTestSetup.SampleRequest, CancellationToken.None);
        }
        var eventsAfterCreate = _events.Published.Count;

        using var callerCancellation = new CancellationTokenSource();
        var ghn = new CancelAfterGhnClient(_fake, callerCancellation);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = QuoteTestSetup.CreateService(scope.ServiceProvider, ghn, _logger, _events);
            await service.CancelShipmentAsync(shipment.Id, callerCancellation.Token);
        }

        Assert.True(callerCancellation.IsCancellationRequested);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal(ShipmentStatus.Cancelled, stored.Status);
        Assert.Equal("ship07-ref-1#cancelled#1", stored.Ref);
        var history = await ShipmentTestSetup.ReadEventsAsync(factory);
        Assert.Single(history, row => row.Status == ShipmentStatus.Cancelled);
        Assert.Equal(eventsAfterCreate + 1, _events.Published.Count);
    }
}
