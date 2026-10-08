namespace Pickory.Modules.Catalog.Contracts.Reading;

// Tóm tắt một sản phẩm cho lưới danh mục và trang so sánh. `Image` là ảnh ở vị trí đầu tiên.
public sealed record ProductSummary(
    string Id,
    string Slug,
    string Name,
    BrandRef Brand,
    int? MinPrice,
    bool InStock,
    string Status,
    ProductImageInfo? Image);
