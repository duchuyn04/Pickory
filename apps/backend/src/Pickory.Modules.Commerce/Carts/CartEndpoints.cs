using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Carts;

// Bốn route giỏ hàng, dùng được cho khách vãng lai và tài khoản, nên route không đòi quyền.
// Route ghi vẫn đi qua kiểm `Origin` của actor middleware, như mọi route ghi công khai.
internal static class CartEndpoints
{
    private const string CartPath = "/api/cart";
    private const string ItemsPath = "/api/cart/items";
    private const string ItemPath = "/api/cart/items/{skuId}";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(CartPath, GetAsync);
        endpoints.MapPost(ItemsPath, AddItemAsync);
        endpoints.MapPatch(ItemPath, SetQuantityAsync);
        endpoints.MapDelete(ItemPath, RemoveItemAsync);
    }

    private static async Task<IResult> GetAsync(HttpContext context, CartService service)
    {
        var cart = await service.GetAsync(OwnerOf(context), context.RequestAborted);
        return Results.Ok(new ApiResponse<CartResponse>(cart));
    }

    private static async Task<IResult> AddItemAsync(HttpContext context, JsonElement? body, CartService service)
    {
        // Kiểm body trước mọi truy cập DB. Body sai không gọi catalog và không tạo giỏ.
        var input = CartRequests.ReadAddItem(body);
        var cart = await service.AddItemAsync(OwnerOf(context), input, context.RequestAborted);
        return Results.Ok(new ApiResponse<CartResponse>(cart));
    }

    private static async Task<IResult> SetQuantityAsync(
        HttpContext context, string skuId, JsonElement? body, CartService service)
    {
        var quantity = CartRequests.ReadQuantityChange(body);
        var cart = await service.SetQuantityAsync(OwnerOf(context), skuId, quantity, context.RequestAborted);
        return Results.Ok(new ApiResponse<CartResponse>(cart));
    }

    private static async Task<IResult> RemoveItemAsync(HttpContext context, string skuId, CartService service)
    {
        var cart = await service.RemoveItemAsync(OwnerOf(context), skuId, context.RequestAborted);
        return Results.Ok(new ApiResponse<CartResponse>(cart));
    }

    // Route checkout đọc đúng giỏ này, nên hàm dùng chung thay vì lặp luật chọn chủ giỏ.
    internal static CartOwner OwnerOf(HttpContext context)
    {
        // Actor luôn có `AnonymousId`. `UserId` chỉ có khi khách đã đăng nhập.
        var actor = context.GetActor();
        return new CartOwner(actor.UserId, actor.AnonymousId);
    }
}
