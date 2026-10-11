using System.Text.Json;

namespace Pickory.Modules.Commerce.Entities;

// Một dòng hàng của đơn, chụp lại tên, mã, tùy chọn và giá lúc đặt. `SkuId` và `ProductId` không có khóa ngoại sang catalog.
public sealed class OrderItem
{
    public string Id { get; set; } = null!;
    public string OrderId { get; set; } = string.Empty;
    public string SkuId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string NameSnapshot { get; set; } = string.Empty;
    public string SkuCodeSnapshot { get; set; } = string.Empty;
    public JsonDocument OptionValues { get; set; } = null!;
    public string? ImagePublicId { get; set; }
    public int UnitPrice { get; set; }
    public int Quantity { get; set; }

    public Order? Order { get; set; }
}
