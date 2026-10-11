namespace Pickory.Modules.Shipping.Contracts.Shipments;

// Vận đơn đã tạo. `Ref` là mã đơn gốc của bên gọi, và `TrackingCode` là mã vận đơn của GHN.
public sealed record Shipment(string Id, string Ref, string TrackingCode, int Fee, ShipmentStatus Status);
