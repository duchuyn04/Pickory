namespace Pickory.Modules.Shipping.Contracts.Shipments;

// Trạng thái chuẩn của vận đơn, không phụ thuộc đơn vị vận chuyển. Trạng thái lạ không phải giá trị lưu được.
public enum ShipmentStatus
{
    PendingPickup,
    InTransit,
    Delivering,
    Delivered,
    DeliveryFailed,
    Returning,
    Returned,
    Cancelled,
    Exception,
}
