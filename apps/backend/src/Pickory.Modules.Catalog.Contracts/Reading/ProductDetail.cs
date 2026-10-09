using System.Text.Json;

namespace Pickory.Modules.Catalog.Contracts.Reading;

// Chi tiết một sản phẩm khách thấy được. `Status` là `active` hoặc `discontinued`.
public sealed record ProductDetail(
    string Id,
    string Slug,
    string Name,
    string Status,
    string ShortDesc,
    JsonElement Description,
    int WarrantyMonths,
    BrandRef Brand,
    CategoryRef Category,
    CategoryRef? ParentCategory,
    IReadOnlyList<SpecEntry> Specs,
    IReadOnlyList<ProductOptionInfo> Options,
    IReadOnlyList<ProductSkuInfo> Skus,
    IReadOnlyList<ProductImageInfo> Images,
    int? MinPrice,
    bool InStock);

public sealed record BrandRef(string Id, string Name, string Slug);

public sealed record CategoryRef(string Id, string Name, string Slug);

// Một thông số kèm định nghĩa thuộc tính. `Value` là `null` khi sản phẩm chưa có giá trị cho thuộc tính này.
public sealed record SpecEntry(
    string Key,
    string Label,
    string Group,
    string Type,
    string? Unit,
    JsonElement? Value);

public sealed record ProductOptionInfo(string Name, IReadOnlyList<string> Values);

// SKU đang bật của sản phẩm. Khách chỉ biết còn hàng hay không, không biết số tồn chính xác.
public sealed record ProductSkuInfo(
    string Id,
    string Code,
    IReadOnlyDictionary<string, string> OptionValues,
    int Price,
    int? CompareAtPrice,
    bool InStock);

// `SkuId` khác `null` khi ảnh gắn riêng cho một SKU.
public sealed record ProductImageInfo(string Url, string Alt, string? SkuId);
