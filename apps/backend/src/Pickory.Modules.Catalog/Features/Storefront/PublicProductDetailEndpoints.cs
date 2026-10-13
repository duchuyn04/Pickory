using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Route công khai của chi tiết và so sánh sản phẩm. Khách vãng lai gọi được, không cần quyền back-office.
public static class PublicProductDetailEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Route chữ `compare` thắng route `{slug}` theo thứ tự ưu tiên của ASP.NET Core, nên không bị coi là một slug.
        endpoints.MapGet("/api/products/compare", CompareAsync);
        endpoints.MapGet("/api/products/{slug}", GetProductAsync);
        // Đặt route số nhiều tách khỏi `{slug}` để sản phẩm tên `product-details` không bị che.
        endpoints.MapGet("/api/product-details", GetProductsBySlugsAsync);
    }

    private static async Task<IResult> GetProductAsync(
        string slug, HttpContext context, ProductDetailService detailService)
    {
        var lookup = await detailService.LookupBySlugAsync(slug, context.RequestAborted);
        switch (lookup)
        {
            case ProductSlugLookup.Found found:
                return Results.Ok(new ApiResponse<Contracts.Reading.ProductDetail>(found.Detail));

            case ProductSlugLookup.Moved moved:
                // Giữ query string để liên kết cũ có tham số vẫn tới đúng trạng thái. Redirect vĩnh viễn (301) báo công cụ tìm kiếm đổi địa chỉ.
                var location = $"/api/products/{Uri.EscapeDataString(moved.CurrentSlug)}{context.Request.QueryString}";
                return Results.Redirect(location, permanent: true);

            default:
                // Cùng một lỗi cho slug lạ, `draft` và `hidden`. Khách không biết sản phẩm có tồn tại hay không.
                throw new AppException("PRODUCT_NOT_FOUND", 404, "Không tìm thấy sản phẩm.");
        }
    }

    private static async Task<IResult> GetProductsBySlugsAsync(
        HttpContext context, ProductDetailService detailService)
    {
        // Giới hạn số tham số trước khi truy vấn, để một request không nạp quá nhiều chi tiết.
        var rawSlugs = context.Request.Query["slug"];
        if (rawSlugs.Count > 20)
        {
            throw new ValidationException(
                [new ValidationFailure("slug", "Chỉ tra được tối đa 20 sản phẩm.")]);
        }

        // Bỏ slug trống và trùng, giữ thứ tự đầu tiên vì thứ tự DB không theo thứ tự URL.
        var slugs = new List<string>(rawSlugs.Count);
        var seenSlugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slug in rawSlugs)
        {
            if (string.IsNullOrWhiteSpace(slug) || !seenSlugs.Add(slug))
            {
                continue;
            }

            slugs.Add(slug);
        }
        if (slugs.Count == 0)
        {
            throw new ValidationException(
                [new ValidationFailure("slug", "Cần ít nhất một slug sản phẩm.")]);
        }

        // Bộ đọc chung dựng đúng hợp đồng chi tiết đơn và bỏ slug không còn công khai.
        var details = await detailService.FindBySlugsAsync(slugs, context.RequestAborted);
        return Results.Ok(new ApiResponse<IReadOnlyList<Contracts.Reading.ProductDetail>>(details));
    }

    private static async Task<IResult> CompareAsync(
        HttpContext context, ProductComparisonService comparisonService)
    {
        // Tham số `ids` lặp (`ids=a&ids=b`) được nối thành một danh sách cùng dạng với `ids=a,b`.
        var rawIds = string.Join(',', context.Request.Query["ids"].ToArray());
        var ids = CompareIdsReader.Read(rawIds);

        var comparison = await comparisonService.CompareAsync(ids, context.RequestAborted);
        return Results.Ok(new ApiResponse<ProductComparisonResponse>(comparison));
    }
}
