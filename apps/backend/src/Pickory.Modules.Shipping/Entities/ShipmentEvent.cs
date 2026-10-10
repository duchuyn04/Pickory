using Pickory.Modules.Shipping.Contracts.Shipments;

namespace Pickory.Modules.Shipping.Entities;

// Một lần đổi trạng thái của vận đơn. `RawStatus` giữ trạng thái gốc của đơn vị vận chuyển.
public sealed class ShipmentEvent
{
    public string Id { get; set; } = null!;
    public string ShipmentId { get; set; } = string.Empty;
    public ShipmentStatus Status { get; set; }
    public string RawStatus { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public ShipmentEventSource Source { get; set; }

    public Shipment? Shipment { get; set; }
}
