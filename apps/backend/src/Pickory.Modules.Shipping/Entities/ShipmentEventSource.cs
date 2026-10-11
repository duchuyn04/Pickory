namespace Pickory.Modules.Shipping.Entities;

// Nguồn của lần đổi trạng thái: webhook của GHN, job đồng bộ, hoặc nhân viên sửa tay.
public enum ShipmentEventSource
{
    Webhook,
    Poll,
    Manual,
}
