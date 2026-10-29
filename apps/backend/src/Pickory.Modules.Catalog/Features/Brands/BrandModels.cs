namespace Pickory.Modules.Catalog.Features.Brands;

// Dữ liệu phản hồi của một thương hiệu sản phẩm.
public sealed record BrandResponse(
    string Id,
    string Name,
    string Slug,
    string? LogoPublicId,
    string? LogoUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// Yêu cầu tạo mới thương hiệu từ trang quản trị.
public sealed record CreateBrandRequest(
    string Name,
    string? LogoPublicId);

// Yêu cầu cập nhật thông tin thương hiệu từ trang quản trị.
public sealed record UpdateBrandRequest(
    string? Name,
    string? LogoPublicId);
