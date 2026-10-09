namespace Pickory.Modules.Catalog.Contracts.Reading;

// Bộ lọc của `ICatalogReader.ListProductsAsync`, cùng ý nghĩa với tham số của `GET /api/products`.
// `Attributes` dùng khóa không kèm tiền tố `attr.`, và `Sort` nhận `newest`, `price_asc` hoặc `price_desc`.
public sealed record ProductFilters(
    string? CategorySlug = null,
    IReadOnlyList<string>? BrandSlugs = null,
    int? PriceMin = null,
    int? PriceMax = null,
    bool InStockOnly = false,
    IReadOnlyDictionary<string, string>? Attributes = null,
    string? Sort = null);
