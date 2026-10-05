using System.Text.Json;

namespace Pickory.Modules.Catalog.Entities;

// Sản phẩm điện tử với đầy đủ thông số kỹ thuật và tùy chọn cấu hình.
public sealed class Product
{
    public string Id { get; set; } = null!;
    public string CategoryId { get; set; } = string.Empty;
    public string BrandId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ShortDesc { get; set; } = string.Empty;
    public JsonDocument Description { get; set; } = null!;
    public JsonDocument Specs { get; set; } = null!;
    public JsonDocument Options { get; set; } = null!;
    public int WarrantyMonths { get; set; } = 12;
    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public int? MinPrice { get; set; }
    public bool InStock { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category? Category { get; set; }
    public Brand? Brand { get; set; }
    public ICollection<Sku> Skus { get; set; } = new List<Sku>();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductSlugHistory> SlugHistory { get; set; } = new List<ProductSlugHistory>();
}
