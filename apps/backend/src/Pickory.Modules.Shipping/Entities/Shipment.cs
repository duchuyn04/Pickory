using Pickory.Modules.Shipping.Contracts.Parcels;
using Pickory.Modules.Shipping.Contracts.Shipments;

namespace Pickory.Modules.Shipping.Entities;

// Một vận đơn gửi qua đơn vị vận chuyển. `Ref` là mã đơn, mỗi đơn có tối đa một vận đơn còn hiệu lực.
public sealed class Shipment
{
    public string Id { get; set; } = null!;
    public string Ref { get; set; } = string.Empty;
    public string Carrier { get; set; } = "ghn";
    public string TrackingCode { get; set; } = string.Empty;
    public int Fee { get; set; }
    public int CodAmount { get; set; }
    public Parcel Parcel { get; set; } = null!;
    public ShipmentStatus Status { get; set; }
    public string RawStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<ShipmentEvent> Events { get; set; } = [];
}
