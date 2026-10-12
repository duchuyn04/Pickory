using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Commerce.Carts;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
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
        // Chỉ khách đã đăng nhập mới được xem lịch sử đơn hàng của tài khoản.
        endpoints.MapGet(OrdersPath, GetListAsync).RequireAuth();
        endpoints.MapGet($"{OrdersPath}/{{code}}", GetDetailAsync);
        endpoints.MapGet($"{OrdersPath}/{{code}}/payment", GetPaymentAsync);
        // Khách vãng lai dùng token để tự hủy, nên route không yêu cầu đăng nhập.
        // Quyền được kiểm trong service, và actor middleware kiểm Origin để chặn CSRF.
        endpoints.MapPost($"{OrdersPath}/{{code}}/cancellation", CancelAsync);
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

    private static async Task<IResult> GetListAsync(HttpContext context, OrderTrackingService service)
    {
        SetNoStore(context);

        // Đọc tham số phân trang từ query string và kiểm tra tính hợp lệ.
        var failures = new List<ValidationFailure>();
        var page = PageQuery.Read(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // Lấy định danh tài khoản đã xác thực để lọc danh sách đơn hàng.
        var userId = context.GetActor().UserId!;
        var result = await service.GetListAsync(userId, page, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<OrderListItemResponse>>(result));
    }

    private static async Task<IResult> GetDetailAsync(string code, HttpContext context, OrderTrackingService service)
    {
        SetNoStore(context);

        // Đọc thông tin người gọi và token trong query string để kiểm tra quyền truy cập.
        var actor = context.GetActor();
        var token = ReadToken(context);
        var result = await service.GetDetailAsync(code, actor.UserId, token, context.RequestAborted);
        return Results.Ok(new ApiResponse<OrderDetailResponse>(result));
    }

    private static async Task<IResult> GetPaymentAsync(string code, HttpContext context, OrderTrackingService service)
    {
        SetNoStore(context);

        // Đọc thông tin người gọi và token trong query string để xác thực quyền xem.
        var actor = context.GetActor();
        var token = ReadToken(context);
        var result = await service.GetPaymentAsync(code, actor.UserId, token, context.RequestAborted);
        return Results.Ok(new ApiResponse<OrderPaymentResponse>(result));
    }

    private static async Task<IResult> CancelAsync(string code, HttpContext context, OrderTrackingService service)
    {
        SetNoStore(context);

        // Đọc thông tin người gọi và token trong query string để kiểm tra quyền hủy đơn.
        var actor = context.GetActor();
        var token = ReadToken(context);
        var result = await service.CancelAsync(code, actor.UserId, token, context.RequestAborted);
        return Results.Ok(new ApiResponse<OrderDetailResponse>(result));
    }

    private static void SetNoStore(HttpContext context)
    {
        // Chặn lưu bộ nhớ đệm vì phản hồi chứa dữ liệu cá nhân hoặc token truy cập.
        context.Response.Headers.CacheControl = "no-store";
    }

    private static string? ReadToken(HttpContext context)
    {
        // Đọc giá trị token truy cập từ tham số `t` nếu có trong URL.
        return context.Request.Query.TryGetValue("t", out var value) ? value.ToString() : null;
    }
}
