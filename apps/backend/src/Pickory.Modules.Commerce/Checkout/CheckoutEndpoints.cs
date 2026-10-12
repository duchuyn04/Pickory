using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Commerce.Carts;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Checkout;

// Route checkout dùng được cho khách vãng lai và tài khoản, nên route không đòi quyền.
// Route ghi vẫn đi qua kiểm `Origin` của actor middleware, như mọi route ghi công khai.
internal static class CheckoutEndpoints
{
    private const string PreviewsPath = "/api/checkout/previews";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(PreviewsPath, PreviewAsync);
    }

    private static async Task<IResult> PreviewAsync(
        HttpContext context,
        JsonElement? body,
        CheckoutPreviewRateLimiter rateLimiter,
        CheckoutPreviewService service)
    {
        // Đếm lượt trước mọi việc khác. Mỗi preview gọi GHN, nên request lỗi cũng phải tốn lượt.
        var actor = context.GetActor();
        rateLimiter.Consume(CheckoutPreviewRateLimiter.BuildKey(actor.UserId, context.GetClientIp()));

        // Kiểm body trước mọi truy cập DB. Body sai không tra danh mục và không gọi shipping.
        var input = CheckoutPreviewRequests.Read(body);

        var preview = await service.PreviewAsync(CartEndpoints.OwnerOf(context), input, context.RequestAborted);
        return Results.Ok(new ApiResponse<CheckoutPreviewResponse>(preview));
    }
}
