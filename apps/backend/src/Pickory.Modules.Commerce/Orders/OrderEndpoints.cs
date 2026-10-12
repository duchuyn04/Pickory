using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Commerce.Carts;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders;

// Route đặt đơn dùng được cho khách vãng lai và tài khoản, nên route không đòi quyền.
// Route ghi vẫn đi qua kiểm `Origin` của actor middleware, như mọi route ghi công khai.
internal static class OrderEndpoints
{
    private const string OrdersPath = "/api/orders";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(OrdersPath, PlaceAsync);
    }

    private static async Task<IResult> PlaceAsync(HttpContext context, JsonElement? body, PlaceOrderService service)
    {
        // Kiểm header và body trước mọi truy cập DB. Request sai không tính tiền và không gọi shipping.
        var idempotencyKey = IdempotencyKeyRules.Read(context.Request.Headers[IdempotencyKeyRules.HeaderName].ToString());
        var input = PlaceOrderRequests.Read(body);

        var result = await service.PlaceAsync(CartEndpoints.OwnerOf(context), idempotencyKey, input, context.RequestAborted);

        // Đơn mới trả 201. Gửi lại cùng khóa trả 200 với đơn đã có, để client phân biệt hai trường hợp.
        var statusCode = result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK;
        return Results.Json(new ApiResponse<PlaceOrderResponse>(result.Order), statusCode: statusCode);
    }
}
