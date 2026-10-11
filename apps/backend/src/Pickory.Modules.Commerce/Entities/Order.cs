using System.Text.Json;
using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Entities;

// Đơn hàng với dữ liệu chụp lại tại thời điểm đặt.
// Mọi số tiền là đồng Việt Nam kiểu `int`, và `Total` bằng `Subtotal` cộng `ShippingFee` trừ `ShippingDiscount`.
public sealed class Order
{
    public string Id { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string AnonymousId { get; set; } = string.Empty;
    public JsonDocument Contact { get; set; } = null!;
    public JsonDocument ShippingAddress { get; set; } = null!;
    public JsonDocument? InvoiceInfo { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public List<string> Flags { get; set; } = [];
    public int Subtotal { get; set; }
    public int ShippingFee { get; set; }
    public int ShippingDiscount { get; set; }
    public int Total { get; set; }
    public DateTime? PaymentDeadline { get; set; }
    public string AccessTokenHash { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = [];
    public List<OrderStatusHistory> StatusHistory { get; set; } = [];
}
