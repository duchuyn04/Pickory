using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

// Cả Contracts và Entities có kiểu `Shipment`. Test này làm việc với kiểu của bên gọi.
using Shipment = Pickory.Modules.Shipping.Contracts.Shipments.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-07 lát 4: hủy vận đơn, GHN từ chối hủy, hủy lần hai và tạo lại sau khi hủy.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShipmentCancelTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly RecordingGhnClient _ghn = new(new FakeGhnClient());
    private readonly RecordingEventDispatcher _events = new();
    private readonly ListLogger<ShippingService> _logger = new();

    private FakeGhnClient Fake => (FakeGhnClient)_ghn.Inner;

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
        var service = QuoteTestSetup.CreateService(scope.ServiceProvider, _ghn, _logger, _events);
        return await action(service);
    }

    private Task<Shipment> CreateAsync(string reference = "ship07-ref-1") =>
        WithServiceAsync(service => service.CreateShipmentAsync(
            ShipmentTestSetup.CreateRequest(reference), CancellationToken.None));

    private Task CancelAsync(string shipmentId) =>
        WithServiceAsync(async service =>
        {
            await service.CancelShipmentAsync(shipmentId, CancellationToken.None);
            return true;
        });

    private IEnumerable<ShipmentStatusChangedEvent> PublishedEvents() =>
        _events.Published.OfType<ShipmentStatusChangedEvent>();

    [Fact]
    public async Task Cancel_Success_CancelsShipmentRenamesRefWritesOneEventAndPublishesOnce()
    {
        var shipment = await CreateAsync();

        await CancelAsync(shipment.Id);

        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal(ShipmentStatus.Cancelled, stored.Status);
        Assert.Equal("cancel", stored.RawStatus);
        Assert.Equal("ship07-ref-1#cancelled#1", stored.Ref);

        // Lịch sử có dòng tạo và đúng một dòng hủy.
        var history = await ShipmentTestSetup.ReadEventsAsync(factory);
        Assert.Equal(2, history.Count);
        var cancelRow = Assert.Single(history, row => row.Status == ShipmentStatus.Cancelled);
        Assert.Equal("cancel", cancelRow.RawStatus);
        Assert.Equal(ShipmentEventSource.Manual, cancelRow.Source);

        // Mã gửi GHN là mã vận đơn đã lưu, với mã lý do "khác" vì hàm hủy không có lý do cụ thể.
        Assert.Equal(shipment.TrackingCode, Assert.Single(_ghn.CancelledOrderCodes));
        Assert.Equal("GHN-CANCEL-OTHER", Assert.Single(_ghn.CancelReasonCodes));

        var cancelled = Assert.Single(PublishedEvents(), published => published.Status == ShipmentStatus.Cancelled);
        Assert.Equal(shipment.Id, cancelled.ShipmentId);
        // `Ref` trong sự kiện là mã gốc của bên gọi, không phải mã đã đổi hậu tố.
        Assert.Equal("ship07-ref-1", cancelled.Ref);
        Assert.Equal(ShipmentStatus.PendingPickup, cancelled.PreviousStatus);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_DoesNotCallGhnOrPublishAgain()
    {
        var shipment = await CreateAsync();
        await CancelAsync(shipment.Id);
        var callsAfterFirstCancel = _ghn.TotalCallCount;
        var eventsAfterFirstCancel = _events.Published.Count;

        await CancelAsync(shipment.Id);

        Assert.Equal(callsAfterFirstCancel, _ghn.TotalCallCount);
        Assert.Equal(eventsAfterFirstCancel, _events.Published.Count);
        Assert.Equal(2, (await ShipmentTestSetup.ReadEventsAsync(factory)).Count);
        Assert.Equal("ship07-ref-1#cancelled#1", Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory)).Ref);
    }

    [Fact]
    public async Task Cancel_GhnRejects_KeepsStatusAndRefAndPublishesNothing()
    {
        var shipment = await CreateAsync();
        var eventsAfterCreate = _events.Published.Count;
        Fake.SetFailure(FakeGhnOperation.CancelOrder, FakeGhnFailure.Rejected);

        var exception = await Assert.ThrowsAsync<ShippingRejectedException>(() => CancelAsync(shipment.Id));

        Assert.Equal("SHIPPING_REJECTED", exception.Code);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal(ShipmentStatus.PendingPickup, stored.Status);
        Assert.Equal("ship07-ref-1", stored.Ref);
        Assert.Single(await ShipmentTestSetup.ReadEventsAsync(factory));
        Assert.Equal(eventsAfterCreate, _events.Published.Count);
    }

    [Fact]
    public async Task Cancel_NetworkError_ThrowsUnavailableAndKeepsShipment()
    {
        var shipment = await CreateAsync();
        Fake.SetFailure(FakeGhnOperation.CancelOrder, FakeGhnFailure.Unavailable);

        await Assert.ThrowsAsync<ShippingUnavailableException>(() => CancelAsync(shipment.Id));

        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal(ShipmentStatus.PendingPickup, stored.Status);
        // Hủy chỉ gọi GHN một lần, không thử lại.
        Assert.Single(_ghn.CancelledOrderCodes);
    }

    [Fact]
    public async Task Cancel_UnknownShipment_ThrowsNotFound()
    {
        var exception = await Assert.ThrowsAsync<ShipmentNotFoundException>(
            () => CancelAsync(Guid.NewGuid().ToString()));

        Assert.Equal("SHIPMENT_NOT_FOUND", exception.Code);
        Assert.Equal(404, exception.StatusCode);
        Assert.Equal(0, _ghn.TotalCallCount);
    }

    [Theory]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Returned)]
    public async Task Cancel_TerminalShipment_ThrowsRejectedAndDoesNotCallGhn(ShipmentStatus status)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            shipping.Shipments.Add(new Pickory.Modules.Shipping.Entities.Shipment
            {
                Id = "ship07-terminal",
                Ref = "ship07-terminal-ref",
                TrackingCode = "GHNTERMINAL1",
                Fee = 1000,
                CodAmount = 0,
                Parcel = new Parcel(500, 10, 10, 10),
                Status = status,
                RawStatus = status == ShipmentStatus.Delivered ? "delivered" : "returned",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await shipping.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<ShippingRejectedException>(() => CancelAsync("ship07-terminal"));

        Assert.Equal(0, _ghn.TotalCallCount);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal(status, stored.Status);
        Assert.Equal("ship07-terminal-ref", stored.Ref);
    }

    [Fact]
    // Sau khi hủy, `ref` gốc rảnh. Tạo lại gửi mã `-2`, rồi hủy lần hai và tạo lại gửi mã `-3`.
    public async Task Create_AfterCancel_UsesNewSuffixedClientOrderCode()
    {
        var first = await CreateAsync();
        await CancelAsync(first.Id);

        var second = await CreateAsync();

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.TrackingCode, second.TrackingCode);
        Assert.Equal("ship07-ref-1", second.Ref);
        Assert.Equal(["ship07-ref-1", "ship07-ref-1-2"], _ghn.CreateOrderRequests.Select(sent => sent.ClientOrderCode));

        await CancelAsync(second.Id);
        var third = await CreateAsync();

        Assert.Equal("ship07-ref-1", third.Ref);
        Assert.Equal(
            ["ship07-ref-1", "ship07-ref-1-2", "ship07-ref-1-3"],
            _ghn.CreateOrderRequests.Select(sent => sent.ClientOrderCode));

        // Ba vận đơn: hai đã hủy với hậu tố #1 và #2, một còn hiệu lực giữ `ref` gốc.
        var refs = (await ShipmentTestSetup.ReadShipmentsAsync(factory)).Select(shipment => shipment.Ref).ToList();
        Assert.Equal(
            ["ship07-ref-1", "ship07-ref-1#cancelled#1", "ship07-ref-1#cancelled#2"],
            refs);
    }
}
