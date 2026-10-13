using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Danh sách và chi tiết đơn của nhân viên. Cả hai route cùng cần `orders:read`.
internal static class AdminOrderEndpoints
{
    private const string OrdersPath = "/api/admin/orders";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(OrdersPath, GetListAsync).RequirePermission(Permissions.OrdersRead);
        endpoints.MapGet($"{OrdersPath}/{{code}}", GetDetailAsync).RequirePermission(Permissions.OrdersRead);
    }

    private static async Task<IResult> GetDetailAsync(string code, HttpContext context, AdminOrderService service)
    {
        // Chi tiết có liên hệ, địa chỉ và thông tin hóa đơn của khách, nên không cho lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";

        var result = await service.GetDetailAsync(code, context.RequestAborted);
        return Results.Ok(new ApiResponse<AdminOrderDetailResponse>(result));
    }

    private static async Task<IResult> GetListAsync(HttpContext context, AdminOrderService service)
    {
        // Response có tên, số điện thoại của khách, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";

        // Kiểm toàn bộ query string trước khi chạm DB. Tham số sai không tốn một truy vấn.
        var failures = new List<ValidationFailure>();
        var filter = AdminOrderListQuery.Read(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var result = await service.GetListAsync(filter, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<AdminOrderListItemResponse>>(result));
    }
}
