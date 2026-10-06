namespace Pickory.Modules.Catalog.Entities;

// Biến động số lượng tồn kho theo đơn hàng hoặc kiểm kê thủ công.
public sealed class InventoryMovement
{
    public string Id { get; set; } = null!;
    public string SkuId { get; set; } = string.Empty;
    public int Delta { get; set; }
    public InventoryMovementType Type { get; set; }
    public string? OrderId { get; set; }
    public string? ActorId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Sku? Sku { get; set; }
}
