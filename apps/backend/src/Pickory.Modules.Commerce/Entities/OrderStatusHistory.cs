using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Entities;

// Một lần đổi trạng thái đơn. `ActorId` là null khi hệ thống hoặc khách vãng lai chuyển.
public sealed class OrderStatusHistory
{
    public string Id { get; set; } = null!;
    public string OrderId { get; set; } = string.Empty;
    public OrderStatus FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string? ActorId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }

    public Order? Order { get; set; }
}
