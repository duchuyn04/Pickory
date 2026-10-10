namespace Pickory.Modules.Shipping.Contracts.Shipments;

// Một dòng hàng trong vận đơn. GHN dùng tên và số lượng để in mô tả hàng hóa trên phiếu.
public sealed record ShipmentLine(string Name, int Quantity);
