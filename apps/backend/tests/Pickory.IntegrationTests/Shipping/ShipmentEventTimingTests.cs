using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-07: sự kiện chỉ được phát sau commit. Bên nhận đọc DB bằng kết nối khác phải thấy dữ liệu mới.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShipmentEventTimingTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly FakeGhnClient _ghn = new();
    private readonly RecordingEventDispatcher _events = new();

    public async Task InitializeAsync()
    {
        await ShipmentTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
    }

    public Task DisposeAsync() => ShipmentTestSetup.ResetAsync(factory);

    private async Task<T> WithServiceAsync<T>(Func<ShippingService, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = QuoteTestSetup.CreateService(scope.ServiceProvider, _ghn, new ListLogger<ShippingService>(), _events);
        return await action(service);
    }

    // Đọc trạng thái vận đơn bằng một scope và kết nối mới, như bên nhận sự kiện ở request khác.
    private async Task<ShipmentStatus?> ReadStatusFromOtherConnectionAsync(string shipmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var status = await shipping.Shipments
            .AsNoTracking()
            .Where(shipment => shipment.Id == shipmentId)
            .Select(shipment => (ShipmentStatus?)shipment.Status)
            .SingleOrDefaultAsync();
        return status;
    }

    [Fact]
    public async Task Create_EventIsPublishedAfterCommit()
    {
        ShipmentStatus? statusSeenByHandler = null;
        _events.OnPublish = async publishedEvent =>
        {
            var changed = (ShipmentStatusChangedEvent)publishedEvent;
            statusSeenByHandler = await ReadStatusFromOtherConnectionAsync(changed.ShipmentId);
        };

        await WithServiceAsync(service => service.CreateShipmentAsync(
            ShipmentTestSetup.SampleRequest, CancellationToken.None));

        Assert.Equal(ShipmentStatus.PendingPickup, statusSeenByHandler);
    }

    [Fact]
    public async Task Cancel_EventIsPublishedAfterCommit()
    {
        var shipment = await WithServiceAsync(service => service.CreateShipmentAsync(
            ShipmentTestSetup.SampleRequest, CancellationToken.None));
        ShipmentStatus? statusSeenByHandler = null;
        _events.OnPublish = async publishedEvent =>
        {
            var changed = (ShipmentStatusChangedEvent)publishedEvent;
            statusSeenByHandler = await ReadStatusFromOtherConnectionAsync(changed.ShipmentId);
        };

        await WithServiceAsync(async service =>
        {
            await service.CancelShipmentAsync(shipment.Id, CancellationToken.None);
            return true;
        });

        Assert.Equal(ShipmentStatus.Cancelled, statusSeenByHandler);
    }
}
