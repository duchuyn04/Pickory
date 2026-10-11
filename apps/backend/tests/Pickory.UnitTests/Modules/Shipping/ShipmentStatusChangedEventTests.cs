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
        var shipmentEvent = new ShipmentStatusChangedEvent("s1", "ORD-1", ShipmentStatus.InTransit, ShipmentStatus.PendingPickup, OccurredAt);

        Assert.IsAssignableFrom<IDomainEvent>(shipmentEvent);
    }

    [Fact]
    // Lần ghi trạng thái đầu tiên chưa có trạng thái trước, nên `PreviousStatus` cho phép null.
    public void Event_FirstStatus_HasNoPreviousStatus()
    {
        var shipmentEvent = new ShipmentStatusChangedEvent("s1", "ORD-1", ShipmentStatus.PendingPickup, null, OccurredAt);

        Assert.Null(shipmentEvent.PreviousStatus);
        Assert.Equal(ShipmentStatus.PendingPickup, shipmentEvent.Status);
    }
}
