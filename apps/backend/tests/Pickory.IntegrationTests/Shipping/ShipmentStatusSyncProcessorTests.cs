using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Persistence;
using Pickory.Modules.Shipping.Shipments;
using Pickory.Modules.Shipping.Sync;
using Xunit;

// Cả Contracts và Entities có kiểu `Shipment`. Test này làm việc với kiểu của bên gọi.
using Shipment = Pickory.Modules.Shipping.Contracts.Shipments.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-08 lát 4: bộ đồng bộ dự phòng gọi trực tiếp với DB thật và client GHN giả, không chờ đồng hồ.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShipmentStatusSyncProcessorTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly FakeGhnClient _ghn = new();
    private readonly RecordingEventDispatcher _events = new();
    private readonly ListLogger<ShipmentStatusSyncProcessor> _logger = new();

    public async Task InitializeAsync()
    {
        await ShipmentTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
    }

    public Task DisposeAsync() => ShipmentTestSetup.ResetAsync(factory);

    private async Task<Shipment> CreateShipmentAsync(string reference)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = QuoteTestSetup.CreateService(scope.ServiceProvider, _ghn, new ListLogger<ShippingService>(), _events);
        return await service.CreateShipmentAsync(ShipmentTestSetup.CreateRequest(reference), CancellationToken.None);
    }

    private async Task<ShipmentStatusSyncResult> SyncAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var updater = new ShipmentStatusUpdater(database, _events, TimeProvider.System, new ListLogger<ShipmentStatusUpdater>());
        var processor = new ShipmentStatusSyncProcessor(database, _ghn, updater, _logger);
        return await processor.SyncAsync(CancellationToken.None);
    }

    private int StatusEventCount() => _events.Published.OfType<ShipmentStatusChangedEvent>().Count();

    [Fact]
    // Webhook bị lỡ: GHN đã đổi trạng thái nhưng Pickory chưa biết. Vòng đầu cập nhật và phát một sự kiện, vòng hai không phát thêm.
    public async Task Sync_MissedWebhook_UpdatesShipmentAndPublishesExactlyOnce()
    {
        var shipment = await CreateShipmentAsync("ship08-ref-1");
        var eventsAfterCreate = StatusEventCount();
        _ghn.SetOrderStatus(shipment.TrackingCode, "delivering");

        var first = await SyncAsync();
        var second = await SyncAsync();

        Assert.Equal(new ShipmentStatusSyncResult(Checked: 1, Changed: 1, Failed: 0), first);
        Assert.Equal(new ShipmentStatusSyncResult(Checked: 1, Changed: 0, Failed: 0), second);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal(ShipmentStatus.Delivering, stored.Status);
        Assert.Equal(eventsAfterCreate + 1, StatusEventCount());
        var history = await ShipmentTestSetup.ReadEventsAsync(factory);
        Assert.Single(history, row => row.Status == ShipmentStatus.Delivering && row.Source == ShipmentEventSource.Poll);
    }

    [Fact]
    public async Task Sync_TerminalShipments_AreNotCheckedAgainstGhn()
    {
        var delivered = await CreateShipmentAsync("ship08-delivered");
        var cancelled = await CreateShipmentAsync("ship08-cancelled");
        var active = await CreateShipmentAsync("ship08-active");
        _ghn.SetOrderStatus(delivered.TrackingCode, "delivered");
        _ghn.SetOrderStatus(cancelled.TrackingCode, "cancel");
        await SyncAsync();
        var callsBefore = _ghn.OrderDetailCallCount;
        _ghn.SetOrderStatus(active.TrackingCode, "picked");

        var result = await SyncAsync();

        // Chỉ vận đơn còn hiệu lực được tra. Hai vận đơn đã kết thúc không tốn lời gọi GHN.
        Assert.Equal(1, result.Checked);
        Assert.Equal(1, _ghn.OrderDetailCallCount - callsBefore);
        Assert.Equal(1, result.Changed);
    }

    [Fact]
    // Một vận đơn lỗi không chặn cả vòng. Vận đơn lỗi nằm đầu danh sách, các vận đơn sau vẫn được cập nhật.
    public async Task Sync_OneShipmentFails_ContinuesWithTheNextOnes()
    {
        // Mã này không có trong GHN giả nên tra chi tiết bị từ chối. Vận đơn được chèn trước nên cũ nhất.
        await ShipmentTestSetup.InsertShipmentAsync(factory, "ship08-broken", "KHONG-CO-TRONG-GHN");
        var second = await CreateShipmentAsync("ship08-second");
        var third = await CreateShipmentAsync("ship08-third");
        _ghn.SetOrderStatus(second.TrackingCode, "picked");
        _ghn.SetOrderStatus(third.TrackingCode, "delivering");

        var result = await SyncAsync();

        Assert.Equal(new ShipmentStatusSyncResult(Checked: 3, Changed: 2, Failed: 1), result);
        var stored = (await ShipmentTestSetup.ReadShipmentsAsync(factory)).ToDictionary(row => row.Ref);
        Assert.Equal(ShipmentStatus.InTransit, stored["ship08-second"].Status);
        Assert.Equal(ShipmentStatus.Delivering, stored["ship08-third"].Status);
        Assert.Equal(ShipmentStatus.PendingPickup, stored["ship08-broken"].Status);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("KHONG-CO-TRONG-GHN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sync_GhnUnavailable_CountsEveryShipmentAsFailedWithoutThrowing()
    {
        await CreateShipmentAsync("ship08-a");
        await CreateShipmentAsync("ship08-b");
        _ghn.SetFailure(FakeGhnOperation.GetOrderDetail, FakeGhnFailure.Unavailable);

        var result = await SyncAsync();

        Assert.Equal(new ShipmentStatusSyncResult(Checked: 2, Changed: 0, Failed: 2), result);
    }

    [Fact]
    public async Task Sync_UnknownGhnStatus_KeepsStatusAndIsNotCountedAsFailure()
    {
        var shipment = await CreateShipmentAsync("ship08-ref-1");
        _ghn.SetOrderStatus(shipment.TrackingCode, "trang_thai_moi_cua_ghn");

        var result = await SyncAsync();

        Assert.Equal(new ShipmentStatusSyncResult(Checked: 1, Changed: 0, Failed: 0), result);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal("ready_to_pick", stored.RawStatus);
    }

    [Fact]
    // Log của vòng đồng bộ không chứa số điện thoại, tên hay địa chỉ người nhận.
    public async Task Sync_Logs_DoNotContainRecipientData()
    {
        await ShipmentTestSetup.InsertShipmentAsync(factory, "ship08-broken", "KHONG-CO-TRONG-GHN");
        await CreateShipmentAsync("ship08-ref-1");

        await SyncAsync();

        Assert.NotEmpty(_logger.Entries);
        foreach (var entry in _logger.Entries)
        {
            Assert.DoesNotContain("0987654321", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Nguyễn Văn Khách", entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("12 Phố Test", entry.Message, StringComparison.Ordinal);
        }
    }
}
