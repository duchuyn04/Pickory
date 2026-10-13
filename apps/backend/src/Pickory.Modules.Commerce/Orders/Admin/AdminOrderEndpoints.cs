using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Danh sách, chi tiết và thao tác của nhân viên lên đơn, mỗi route cần quyền riêng.
// Route ghi đi qua kiểm `Origin` của actor middleware như mọi route ghi admin khác.
internal static class AdminOrderEndpoints
{
    private const string OrdersPath = "/api/admin/orders";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(OrdersPath, GetListAsync).RequirePermission(Permissions.OrdersRead);
        endpoints.MapGet($"{OrdersPath}/{{code}}", GetDetailAsync).RequirePermission(Permissions.OrdersRead);

        // Mỗi thao tác là một tài nguyên con: xác nhận và hủy tạo mới bằng POST, còn cờ hóa đơn đặt bằng PUT.
        endpoints.MapPost($"{OrdersPath}/{{code}}/confirmation", ConfirmAsync)
            .RequirePermission(Permissions.OrdersUpdateStatus);
        endpoints.MapPost($"{OrdersPath}/{{code}}/cancellation", CancelAsync)
            .RequirePermission(Permissions.OrdersCancel);
        endpoints.MapPut($"{OrdersPath}/{{code}}/invoice-issuance", IssueInvoiceAsync)
            .RequirePermission(Permissions.OrdersUpdateStatus);
    }

    private static async Task<IResult> ConfirmAsync(string code, HttpContext context, AdminOrderActionService service)
    {
        SetNoStore(context);

        // `RequirePermission` chỉ cho nhân viên đi qua, nên actor luôn có `UserId`. Lịch sử và audit cần id thật.
        var actorId = context.GetActor().UserId!;
        var result = await service.ConfirmAsync(code, actorId, context.RequestAborted);
        return Results.Ok(new ApiResponse<AdminOrderDetailResponse>(result));
    }

    private static async Task<IResult> CancelAsync(
        string code, HttpContext context, JsonElement? body, AdminOrderActionService service)
    {
        SetNoStore(context);

        // Kiểm body trước khi mở transaction. Body sai không khóa đơn, không trả kho và không ghi audit.
        var failures = new List<ValidationFailure>();
        var reason = CancelOrderRequest.Read(body, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var actorId = context.GetActor().UserId!;
        var result = await service.CancelAsync(code, actorId, reason, context.RequestAborted);
        return Results.Ok(new ApiResponse<AdminOrderDetailResponse>(result));
    }

    private static async Task<IResult> IssueInvoiceAsync(string code, HttpContext context, AdminOrderActionService service)
    {
        SetNoStore(context);

        var actorId = context.GetActor().UserId!;
        var result = await service.IssueInvoiceAsync(code, actorId, context.RequestAborted);
        return Results.Ok(new ApiResponse<AdminOrderDetailResponse>(result));
    }

    private static void SetNoStore(HttpContext context)
    {
        // Phản hồi có thông tin liên hệ của khách, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";
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
