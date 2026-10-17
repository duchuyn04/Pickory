using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders.Lookup;

// Endpoint công khai cho phép khách tra cứu đơn hàng bằng mã đơn và email.
internal static class OrderLookupEndpoints
{
    private const string LookupPath = "/api/order-lookups";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(LookupPath, HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        JsonElement? body,
        OrderLookupRateLimiter rateLimiter,
        OrderLookupService service)
    {
        // Kiểm tra định dạng body trước mọi bước khác. Request sai định dạng không bị tính lượt gọi.
        var input = OrderLookupRequests.Read(body);

        // Giới hạn tần suất gọi theo địa chỉ IP và email đã chuẩn hóa.
        rateLimiter.CheckAndConsume(context.GetClientIp(), input.Email);

        // Tra cứu đơn hàng và gửi email nếu thông tin khớp.
        await service.LookupAsync(input, context.RequestAborted);

        // Luôn trả mã 202 cùng một thông điệp cố định để bảo vệ dữ liệu đơn hàng.
        return Results.Json(
            new ApiResponse<OrderLookupResponse>(
                new OrderLookupResponse("Nếu thông tin đúng, link xem đơn đã được gửi tới email của đơn.")),
            statusCode: StatusCodes.Status202Accepted);
    }
}
