using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Shared.Exceptions;
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

        // Tra cứu đơn. Không khớp mã hoặc email đều trả cùng một lỗi để không lộ trường nào sai.
        var result = await service.LookupAsync(input, context.RequestAborted);
        if (result is null)
        {
            throw new AppException("ORDER_NOT_FOUND", 404, "Không tìm thấy đơn hàng với mã và email này.");
        }

        // Trả token để trình duyệt chuyển ngay tới trang chi tiết đơn.
        return Results.Ok(new ApiResponse<OrderLookupResponse>(result));
    }
}
