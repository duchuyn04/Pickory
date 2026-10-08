using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Catalog.Persistence;

namespace Pickory.Modules.Catalog.Features.Storefront;

// Trang HTML công khai `/p/{slug}` và `/c/{slug}`: template của SPA kèm thẻ meta SEO. Khách vãng lai mở được, không cần quyền.
public static class PublicPageEndpoints
{
    private const string HtmlContentType = "text/html; charset=utf-8";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/p/{slug}", GetProductPageAsync);
        endpoints.MapGet("/c/{slug}", GetCategoryPageAsync);
    }

    private static async Task<IResult> GetProductPageAsync(
        string slug, HttpContext context, ProductDetailService detailService, WebPageTemplate template)
    {
        var lookup = await detailService.LookupBySlugAsync(slug, context.RequestAborted);
        switch (lookup)
        {
            case ProductSlugLookup.Found found:
                return Html(template, PageMetaFactory.ForProduct(found.Detail, template.WebOrigin), StatusCodes.Status200OK);

            case ProductSlugLookup.Moved moved:
                // Giữ query string và dùng 301 như route API, để công cụ tìm kiếm đổi sang địa chỉ mới.
                var location = $"/p/{Uri.EscapeDataString(moved.CurrentSlug)}{context.Request.QueryString}";
                return Results.Redirect(location, permanent: true);

            default:
                // Trả 404 nhưng vẫn kèm HTML của SPA. SPA chạy tiếp và tự hiện trang "Không tìm thấy sản phẩm".
                return Html(template, PageMetaFactory.NotFound(), StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> GetCategoryPageAsync(
        string slug, HttpContext context, CatalogDbContext database, WebPageTemplate template)
    {
        // Danh mục là dữ liệu công khai, nên chỉ cần tên để dựng meta.
        var name = await database.Categories.AsNoTracking()
            .Where(category => category.Slug == slug)
            .Select(category => category.Name)
            .FirstOrDefaultAsync(context.RequestAborted);
        if (name is null)
        {
            return Html(template, PageMetaFactory.NotFound(), StatusCodes.Status404NotFound);
        }

        return Html(template, PageMetaFactory.ForCategory(name, slug, template.WebOrigin), StatusCodes.Status200OK);
    }

    private static IResult Html(WebPageTemplate template, PageMeta meta, int statusCode) =>
        Results.Content(PageMetaRenderer.Render(template.Html, meta), HtmlContentType, null, statusCode);
}
