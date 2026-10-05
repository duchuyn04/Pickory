using System.Text.Json;

namespace Pickory.Modules.Catalog.Persistence;

// Danh mục sản phẩm tổ chức dạng cây tối đa hai cấp để phân cấp mặt hàng.
public sealed class Category
{
    public string Id { get; set; } = null!;
    public string? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int Position { get; set; }
    public JsonDocument Attributes { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

// Thương hiệu sản xuất các mặt hàng điện tử được bán trên nền tảng.
public sealed class Brand
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoPublicId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}

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

// Lịch sử đường dẫn sản phẩm cũ để hỗ trợ chuyển hướng khi đổi tên.
public sealed class ProductSlugHistory
{
    public string Id { get; set; } = null!;
    public string OldSlug { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Product? Product { get; set; }
}

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

// Tác vụ dọn dẹp tập tin hình ảnh trên dịch vụ lưu trữ ngoài.
public sealed class ImageDeletionJob
{
    public string Id { get; set; } = null!;
    public string PublicId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 8;
    public DateTime NextRetryAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
