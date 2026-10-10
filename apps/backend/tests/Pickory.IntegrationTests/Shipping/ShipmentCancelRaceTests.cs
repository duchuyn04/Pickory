using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Modules.Shipping.Shipments;
using Xunit;

// Cả Contracts và Entities có kiểu `Shipment`. Test này làm việc với kiểu của bên gọi.
using Shipment = Pickory.Modules.Shipping.Contracts.Shipments.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-08: hủy tay và webhook `cancel` cùng ghi. Chỉ một bên được đổi `ref`, ghi lịch sử và phát sự kiện.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShipmentCancelRaceTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly FakeGhnClient _fake = new();
    private readonly RecordingEventDispatcher _events = new();

    public async Task InitializeAsync()
    {
        await ShipmentTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
    }

    public Task DisposeAsync() => ShipmentTestSetup.ResetAsync(factory);

    private async Task<Shipment> CreateShipmentAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = QuoteTestSetup.CreateService(scope.ServiceProvider, _fake, new ListLogger<ShippingService>(), _events);
        return await service.CreateShipmentAsync(ShipmentTestSetup.SampleRequest, CancellationToken.None);
    }

    // Webhook `cancel` ghi trước, trong một scope khác, bằng cùng lớp áp trạng thái mà endpoint webhook dùng.
    private async Task ApplyCancelWebhookAsync(string shipmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var shipment = await database.Shipments.AsNoTracking().SingleAsync(row => row.Id == shipmentId);
        var updater = new ShipmentStatusUpdater(
            database, _events, TimeProvider.System, new ListLogger<ShipmentStatusUpdater>());
        await updater.ApplyAsync(shipment, ShipmentStatusMapper.CancelledRawStatus, ShipmentEventSource.Webhook);
    }

    [Fact]
    // Webhook ghi giữa lời gọi hủy GHN và bước lưu của hủy tay. Hủy tay không được đổi `ref` lần hai hay phát sự kiện lần hai.
    public async Task Cancel_WebhookWritesFirst_ManualCancelReturnsWithoutSecondRenameOrEvent()
    {
        var shipment = await CreateShipmentAsync();
        var cancelEventsBefore = _events.Published.OfType<ShipmentStatusChangedEvent>()
            .Count(published => published.Status == ShipmentStatus.Cancelled);
        var racingClient = new CallbackAfterGhnClient(_fake, () => ApplyCancelWebhookAsync(shipment.Id));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = QuoteTestSetup.CreateService(
                scope.ServiceProvider, racingClient, new ListLogger<ShippingService>(), _events);
            await service.CancelShipmentAsync(shipment.Id, CancellationToken.None);
        }

        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal(ShipmentStatus.Cancelled, stored.Status);
        Assert.Equal("ship07-ref-1#cancelled#1", stored.Ref);
        var history = await ShipmentTestSetup.ReadEventsAsync(factory);
        Assert.Single(history, row => row.Status == ShipmentStatus.Cancelled);
        var cancelledEvents = _events.Published.OfType<ShipmentStatusChangedEvent>()
            .Where(published => published.Status == ShipmentStatus.Cancelled)
            .Skip(cancelEventsBefore)
            .ToList();
        var published = Assert.Single(cancelledEvents);
        Assert.Equal("ship07-ref-1", published.Ref);
    }
}
