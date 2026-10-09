using System.Text.Json;

namespace Pickory.Modules.Catalog.Entities;

// Biến thể bán hàng cụ thể với mã định danh và mức tồn kho độc lập.
public sealed class Sku
{
    public string Id { get; set; } = null!;
    public string ProductId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public JsonDocument OptionValues { get; set; } = null!;
    public int Price { get; set; }
    public int? CompareAtPrice { get; set; }
    public int Stock { get; set; }
    public bool Enabled { get; set; } = true;
    public int? WeightGrams { get; set; }
    public int? LengthCm { get; set; }
    public int? WidthCm { get; set; }
    public int? HeightCm { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Product? Product { get; set; }
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<InventoryMovement> Movements { get; set; } = new List<InventoryMovement>();
}
