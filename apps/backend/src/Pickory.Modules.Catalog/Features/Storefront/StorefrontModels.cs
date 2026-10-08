namespace Pickory.Modules.Catalog.Features.Storefront;

// Phản hồi của `GET /api/products`. Dữ liệu nằm trong `ApiResponse<T>.Data`.
public sealed record ProductListResponse(
    IReadOnlyList<ProductListItem> Items,
    int Page,
    int PageSize,
    int Total,
    ProductFacets Facets);

// Tóm tắt một sản phẩm cho lưới danh mục. Mô tả, thông số và SKU thuộc trang chi tiết.
public sealed record ProductListItem(
    string Id,
    string Slug,
    string Name,
    ProductBrandSummary Brand,
    int? MinPrice,
    bool InStock,
    string Status,
    ProductThumbnail? Image);

public sealed record ProductBrandSummary(string Id, string Name, string Slug);

// Ảnh đại diện là ảnh ở vị trí đầu tiên của sản phẩm.
public sealed record ProductThumbnail(string PublicId, string Alt);

// Số sản phẩm theo từng giá trị lọc. Mỗi nhóm đếm theo kiểu loại trừ chính nó (Decision 12).
public sealed record ProductFacets(IReadOnlyList<BrandFacet> Brands, IReadOnlyList<AttributeFacet> Attributes);

public sealed record BrandFacet(string Slug, string Name, int Count);

// Thuộc tính `enum`, `text`, `boolean` dùng `Values`. Thuộc tính `number` dùng `Min` và `Max`.
public sealed record AttributeFacet(
    string Key,
    string Label,
    string Type,
    string? Unit,
    IReadOnlyList<AttributeFacetValue>? Values,
    decimal? Min,
    decimal? Max);

// `Value` là chuỗi với `enum` và `text`, là boolean với `boolean`.
public sealed record AttributeFacetValue(object Value, int Count);
