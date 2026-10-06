namespace Pickory.Modules.Catalog.Entities;

// Hình ảnh minh họa chi tiết của sản phẩm hoặc biến thể bán hàng.
public sealed class ProductImage
{
    public string Id { get; set; } = null!;
    public string ProductId { get; set; } = string.Empty;
    public string? SkuId { get; set; }
    public string PublicId { get; set; } = string.Empty;
    public string Alt { get; set; } = string.Empty;
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; }

    public Product? Product { get; set; }
    public Sku? Sku { get; set; }
}
