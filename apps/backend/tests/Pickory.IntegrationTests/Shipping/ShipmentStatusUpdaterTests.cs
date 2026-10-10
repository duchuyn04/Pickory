using Microsoft.EntityFrameworkCore;
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
using Xunit;

// Cả Contracts và Entities có kiểu `Shipment`. Test này làm việc với entity khi gọi lớp áp trạng thái.
using ShipmentEntity = Pickory.Modules.Shipping.Entities.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-08 lát 2: lớp áp trạng thái dùng chung cho webhook và job đồng bộ, gọi trực tiếp, không qua HTTP.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShipmentStatusUpdaterTests(PickoryWebApplicationFactory factory) : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly FakeGhnClient _ghn = new();
    private readonly RecordingEventDispatcher _events = new();
    private readonly ListLogger<ShipmentStatusUpdater> _logger = new();

    public async Task InitializeAsync()
    {
        await ShipmentTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
    }

    public Task DisposeAsync() => ShipmentTestSetup.ResetAsync(factory);

    private async Task<Pickory.Modules.Shipping.Contracts.Shipments.Shipment> CreateShipmentAsync(string reference = "ship08-ref-1")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = QuoteTestSetup.CreateService(scope.ServiceProvider, _ghn, new ListLogger<ShippingService>(), _events);
        return await service.CreateShipmentAsync(ShipmentTestSetup.CreateRequest(reference), CancellationToken.None);
    }

    private async Task<ShipmentEntity> ReadAsync(string shipmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        return await shipping.Shipments.AsNoTracking().SingleAsync(row => row.Id == shipmentId);
    }

    private async Task<ShipmentStatusUpdateOutcome> ApplyAsync(
        ShipmentEntity shipment, string rawStatus, ShipmentEventSource source, ILogger<ShipmentStatusUpdater>? logger = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var updater = new ShipmentStatusUpdater(
            scope.ServiceProvider.GetRequiredService<ShippingDbContext>(),
            _events,
            TimeProvider.System,
            logger ?? _logger);
        return await updater.ApplyAsync(shipment, rawStatus, source);
    }

    private IEnumerable<ShipmentStatusChangedEvent> StatusEvents() =>
        _events.Published.OfType<ShipmentStatusChangedEvent>();

    [Fact]
    public async Task Apply_StatusChanges_UpdatesShipmentWritesOneHistoryRowAndPublishesOnce()
    {
        var created = await CreateShipmentAsync();
        var eventsAfterCreate = StatusEvents().Count();

        var outcome = await ApplyAsync(await ReadAsync(created.Id), "picked", ShipmentEventSource.Webhook);

        Assert.Equal(ShipmentStatusUpdateOutcome.StatusChanged, outcome);
        var stored = await ReadAsync(created.Id);
        Assert.Equal(ShipmentStatus.InTransit, stored.Status);
        Assert.Equal("picked", stored.RawStatus);
        var history = await ShipmentTestSetup.ReadEventsAsync(factory);
        Assert.Equal(2, history.Count);
        var row = Assert.Single(history, item => item.Status == ShipmentStatus.InTransit);
        Assert.Equal("picked", row.RawStatus);
        Assert.Equal(ShipmentEventSource.Webhook, row.Source);

        var published = Assert.Single(StatusEvents().Skip(eventsAfterCreate));
        Assert.Equal(created.Id, published.ShipmentId);
        Assert.Equal("ship08-ref-1", published.Ref);
        Assert.Equal(ShipmentStatus.InTransit, published.Status);
        Assert.Equal(ShipmentStatus.PendingPickup, published.PreviousStatus);
    }

    [Fact]
    public async Task Apply_SameRawStatus_DoesNothing()
    {
        var created = await CreateShipmentAsync();
        var eventsAfterCreate = _events.Published.Count;

        var outcome = await ApplyAsync(await ReadAsync(created.Id), "ready_to_pick", ShipmentEventSource.Webhook);

        Assert.Equal(ShipmentStatusUpdateOutcome.NoChange, outcome);
        Assert.Single(await ShipmentTestSetup.ReadEventsAsync(factory));
        Assert.Equal(eventsAfterCreate, _events.Published.Count);
    }

    [Fact]
    public async Task Apply_RawStatusChangesButStatusDoesNot_WritesHistoryRowWithoutEvent()
    {
        var created = await CreateShipmentAsync();
        var eventsAfterCreate = _events.Published.Count;
        var before = await ReadAsync(created.Id);

        var outcome = await ApplyAsync(before, "picking", ShipmentEventSource.Poll);

        Assert.Equal(ShipmentStatusUpdateOutcome.RawStatusUpdated, outcome);
        var stored = await ReadAsync(created.Id);
        Assert.Equal(ShipmentStatus.PendingPickup, stored.Status);
        Assert.Equal("picking", stored.RawStatus);
        Assert.True(stored.UpdatedAt >= before.UpdatedAt);
        var history = await ShipmentTestSetup.ReadEventsAsync(factory);
        Assert.Equal(2, history.Count);
        Assert.Single(history, item => item.RawStatus == "picking" && item.Source == ShipmentEventSource.Poll);
        Assert.Equal(eventsAfterCreate, _events.Published.Count);
    }

    [Fact]
    public async Task Apply_UnknownStatus_LogsWarningAndKeepsShipment()
    {
        var created = await CreateShipmentAsync();
        var eventsAfterCreate = _events.Published.Count;

        var outcome = await ApplyAsync(await ReadAsync(created.Id), "brand_new_ghn_status", ShipmentEventSource.Webhook);

        Assert.Equal(ShipmentStatusUpdateOutcome.UnknownStatus, outcome);
        var stored = await ReadAsync(created.Id);
        Assert.Equal(ShipmentStatus.PendingPickup, stored.Status);
        Assert.Equal("ready_to_pick", stored.RawStatus);
        Assert.Single(await ShipmentTestSetup.ReadEventsAsync(factory));
        Assert.Equal(eventsAfterCreate, _events.Published.Count);
        var warning = Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(created.TrackingCode, warning.Message);
        Assert.Contains("brand_new_ghn_status", warning.Message);
    }

    [Fact]
    public async Task Apply_Cancel_RenamesRefAndEventCarriesOriginalRef()
    {
        var created = await CreateShipmentAsync();
        var eventsAfterCreate = StatusEvents().Count();

        var outcome = await ApplyAsync(await ReadAsync(created.Id), "cancel", ShipmentEventSource.Webhook);

        Assert.Equal(ShipmentStatusUpdateOutcome.StatusChanged, outcome);
        var stored = await ReadAsync(created.Id);
        Assert.Equal(ShipmentStatus.Cancelled, stored.Status);
        Assert.Equal("ship08-ref-1#cancelled#1", stored.Ref);
        var published = Assert.Single(StatusEvents().Skip(eventsAfterCreate));
        Assert.Equal("ship08-ref-1", published.Ref);
        Assert.Equal(ShipmentStatus.Cancelled, published.Status);
        Assert.Equal(ShipmentStatus.PendingPickup, published.PreviousStatus);
    }

    [Fact]
    // Hai bên cùng đọc trạng thái cũ. Bên ghi sau thấy `RawStatus` đã đổi nên không ghi lịch sử và không phát sự kiện.
    public async Task Apply_SecondCallerWithStaleRead_LosesRaceWithoutHistoryOrEvent()
    {
        var created = await CreateShipmentAsync();
        var staleRead = await ReadAsync(created.Id);
        var eventsAfterCreate = StatusEvents().Count();

        var first = await ApplyAsync(staleRead, "picked", ShipmentEventSource.Webhook);
        var second = await ApplyAsync(staleRead, "picked", ShipmentEventSource.Poll);

        Assert.Equal(ShipmentStatusUpdateOutcome.StatusChanged, first);
        Assert.Equal(ShipmentStatusUpdateOutcome.LostRace, second);
        Assert.Equal(2, (await ShipmentTestSetup.ReadEventsAsync(factory)).Count);
        Assert.Single(StatusEvents().Skip(eventsAfterCreate));
    }

    [Fact]
    // Hai bên chạy đồng thời với hai kết nối. Điều kiện `rawStatus` khi ghi chỉ cho đúng một bên thắng.
    public async Task Apply_TwoCallersAtTheSameTime_ExactlyOneWins()
    {
        var created = await CreateShipmentAsync();
        var staleRead = await ReadAsync(created.Id);
        var eventsAfterCreate = StatusEvents().Count();

        var outcomes = await Task.WhenAll(
            ApplyAsync(staleRead, "picked", ShipmentEventSource.Webhook, new ListLogger<ShipmentStatusUpdater>()),
            ApplyAsync(staleRead, "picked", ShipmentEventSource.Poll, new ListLogger<ShipmentStatusUpdater>()));

        Assert.Single(outcomes, outcome => outcome == ShipmentStatusUpdateOutcome.StatusChanged);
        Assert.Single(outcomes, outcome => outcome == ShipmentStatusUpdateOutcome.LostRace);
        Assert.Equal(2, (await ShipmentTestSetup.ReadEventsAsync(factory)).Count);
        Assert.Single(StatusEvents().Skip(eventsAfterCreate));
    }

    [Fact]
    public async Task Apply_EventIsPublishedAfterCommit()
    {
        var created = await CreateShipmentAsync();
        ShipmentStatus? statusSeenByHandler = null;
        _events.OnPublish = async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            statusSeenByHandler = await shipping.Shipments.AsNoTracking()
                .Where(row => row.Id == created.Id)
                .Select(row => (ShipmentStatus?)row.Status)
                .SingleAsync();
        };

        await ApplyAsync(await ReadAsync(created.Id), "delivering", ShipmentEventSource.Webhook);

        Assert.Equal(ShipmentStatus.Delivering, statusSeenByHandler);
    }

    [Fact]
    // Vận đơn đã kết thúc thì trạng thái cuối giữ nguyên. Trạng thái lạ từ GHN không kéo vận đơn đã giao quay lại.
    public async Task Apply_TerminalShipment_IsLeftAlone()
    {
        var created = await CreateShipmentAsync();
        await ApplyAsync(await ReadAsync(created.Id), "delivered", ShipmentEventSource.Webhook);
        var eventsAfterDelivered = _events.Published.Count;

        var outcome = await ApplyAsync(await ReadAsync(created.Id), "picking", ShipmentEventSource.Poll);

        Assert.Equal(ShipmentStatusUpdateOutcome.NoChange, outcome);
        Assert.Equal(ShipmentStatus.Delivered, (await ReadAsync(created.Id)).Status);
        Assert.Equal(eventsAfterDelivered, _events.Published.Count);
    }
}
