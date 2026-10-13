using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping;

// SHIP-02: sự kiện đổi trạng thái vận đơn là sự kiện miền để bộ điều phối của Shared nhận được.
public sealed class ShipmentStatusChangedEventTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Event_IsDomainEvent()
    {
        var shipmentEvent = new ShipmentStatusChangedEvent(
            "s1", "ORD-1", ShipmentStatus.InTransit, ShipmentStatus.PendingPickup, OccurredAt, ShipmentStatusChangeSource.Carrier);

        Assert.IsAssignableFrom<IDomainEvent>(shipmentEvent);
    }

    [Fact]
    // Bên nhận cần biết thay đổi do shop hay do hãng vận chuyển, nên sự kiện mang nguồn thay đổi.
    public void Event_CarriesChangeSource()
    {
        var shipmentEvent = new ShipmentStatusChangedEvent(
            "s1", "ORD-1", ShipmentStatus.Cancelled, ShipmentStatus.PendingPickup, OccurredAt, ShipmentStatusChangeSource.Shop);

        Assert.Equal(ShipmentStatusChangeSource.Shop, shipmentEvent.Source);
    }

    [Fact]
    // Lần ghi trạng thái đầu tiên chưa có trạng thái trước, nên `PreviousStatus` cho phép null.
    public void Event_FirstStatus_HasNoPreviousStatus()
    {
        var shipmentEvent = new ShipmentStatusChangedEvent(
            "s1", "ORD-1", ShipmentStatus.PendingPickup, null, OccurredAt, ShipmentStatusChangeSource.Shop);

        Assert.Null(shipmentEvent.PreviousStatus);
        Assert.Equal(ShipmentStatus.PendingPickup, shipmentEvent.Status);
    }
}
