using Pickory.Modules.Shipping.Contracts.Parcels;

namespace Pickory.Modules.Shipping.Contracts.Shipments;

// Yêu cầu tạo vận đơn, tiền tính bằng đồng. `Ref` là mã đơn gốc và có tối đa một vận đơn còn hiệu lực.
public sealed record CreateShipmentRequest(
    string Ref,
    ShippingRecipient To,
    Parcel Parcel,
    IReadOnlyList<ShipmentLine> Items,
    int CodAmount,
    int InsuredValue,
    string? Note);
