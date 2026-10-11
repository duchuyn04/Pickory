using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Catalog.Caching;
using Pickory.Shared.Http;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Route công khai của danh sách sản phẩm. Khách vãng lai gọi được, không cần đăng nhập hay quyền back-office.
public static class PublicProductsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/products", GetProductsAsync);
    }

    private static async Task<IResult> GetProductsAsync(
        HttpContext context,
        ProductSearchService searchService,
        CatalogResponseCache cache)
    {
        // Gom tham số lặp thành một chuỗi cách nhau bằng dấu phẩy. Giá trị lặp của tham số số bị bộ đọc từ chối.
        var parameters = context.Request.Query.ToDictionary(
            pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);

        var query = ProductSearchQuery.Read(parameters);
        try
        {
            // Khóa theo tham số đã chuẩn hóa. Lỗi không bao giờ được cache, nên `UnknownFilterException` đi thẳng ra ngoài.
            var result = await cache.GetOrCreateAsync(
                CatalogResponseCache.NormalizeQuery(parameters),
                () => searchService.SearchAsync(query, context.RequestAborted));
            return Results.Ok(new ApiResponse<ProductListResponse>(result));
        }
        catch (UnknownFilterException unknownFilter)
        {
            // Trả tên từng khóa sai trong `details`, cùng dạng với `SPECS_INVALID`, để giao diện báo đúng bộ lọc.
            return Results.Json(new
            {
                error = new
                {
                    code = unknownFilter.Code,
                    message = unknownFilter.Message,
                    details = unknownFilter.Keys.Select(key => new
                    {
                        key,
                        message = "Khóa không tồn tại hoặc không dùng để lọc được.",
                    }),
                },
            }, statusCode: unknownFilter.StatusCode);
        }
    }
}
