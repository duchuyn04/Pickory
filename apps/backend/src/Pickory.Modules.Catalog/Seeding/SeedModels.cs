using Pickory.Modules.Catalog.Attributes;
using Pickory.Modules.Catalog.Entities;
using Pickory.Modules.Catalog.Products;

namespace Pickory.Modules.Catalog.Seeding;

// Kích thước đóng gói mặc định của một danh mục. Phí vận chuyển cần các số này ở mọi SKU đăng bán.
public sealed record SeedPackaging(int WeightGrams, int LengthCm, int WidthCm, int HeightCm);

// Một danh mục gốc kèm bộ thuộc tính. Danh mục gốc không có con nên chứa sản phẩm trực tiếp.
public sealed record SeedCategory(string Name, IReadOnlyList<AttributeDefinition> Attributes, SeedPackaging Packaging);

// Một biến thể của sản phẩm seed. `Stock` là tồn kho ban đầu, được ghi bằng movement chứ không gán vào cột `stock`.
public sealed record SeedVariant(
    IReadOnlyDictionary<string, string> OptionValues, int Price, int Stock, int? CompareAtPrice = null);

// Một ảnh sản phẩm lấy từ trang chính hãng. Repo chỉ giữ URL nguồn, còn ảnh được tải lên kho ảnh khi chạy lệnh seed.
public sealed record SeedImage(string SourceUrl);

public sealed record SeedProduct(
    string Name,
    string CategoryName,
    string BrandName,
    string ShortDesc,
    string Description,
    IReadOnlyDictionary<string, object> Specs,
    IReadOnlyList<ProductOption> Options,
    IReadOnlyList<SeedVariant> Variants,
    ProductStatus Status = ProductStatus.Active,
    int WarrantyMonths = 12)
{
    // Ảnh đầu tiên là ảnh đại diện. Thứ tự trong danh sách quyết định số thứ tự trong `publicId`.
    public IReadOnlyList<SeedImage> Images { get; init; } = [];

    // Khóa tự nhiên của sản phẩm. Lần chạy sau tìm lại sản phẩm theo slug này nên không tạo trùng.
    public string Slug => SlugGenerator.ToSlug(Name);
}

public sealed record SeedCatalog(IReadOnlyList<SeedCategory> Categories, IReadOnlyList<SeedProduct> Products)
{
    public IReadOnlyList<string> BrandNames => Products.Select(product => product.BrandName).Distinct().ToList();
}
