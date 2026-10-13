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

        // Nhận tiền COD đặt trạng thái thanh toán bằng PUT, không có body. Quyền thuộc nhóm đối soát thanh toán.
        endpoints.MapPut($"{OrdersPath}/{{code}}/cod-receipt", ReceiveCodAsync)
            .RequirePermission(Permissions.PaymentsReconcile);

        // Hoàn tiền cần mã giao dịch trong body và quyền riêng `refunds:manage`, nên `sales` không gọi được.
        endpoints.MapPut($"{OrdersPath}/{{code}}/refund", MarkRefundedAsync)
            .RequirePermission(Permissions.RefundsManage);

        // Vận đơn là tài nguyên con của đơn: tạo bằng POST và hủy bằng DELETE, cùng quyền `orders:fulfill`.
        endpoints.MapPost($"{OrdersPath}/{{code}}/shipment", CreateShipmentAsync)
            .RequirePermission(Permissions.OrdersFulfill);
        endpoints.MapDelete($"{OrdersPath}/{{code}}/shipment", CancelShipmentAsync)
            .RequirePermission(Permissions.OrdersFulfill);
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

    private static async Task<IResult> ReceiveCodAsync(string code, HttpContext context, AdminOrderActionService service)
    {
        SetNoStore(context);

        var actorId = context.GetActor().UserId!;
        var result = await service.ReceiveCodAsync(code, actorId, context.RequestAborted);
        return Results.Ok(new ApiResponse<AdminOrderDetailResponse>(result));
    }

    private static async Task<IResult> MarkRefundedAsync(
        string code, HttpContext context, JsonElement? body, AdminOrderActionService service)
    {
        SetNoStore(context);

        // Kiểm body trước khi mở transaction. Body sai không khóa đơn và không ghi audit.
        var failures = new List<ValidationFailure>();
        var transactionRef = RefundOrderRequest.Read(body, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var actorId = context.GetActor().UserId!;
        var result = await service.MarkRefundedAsync(code, actorId, transactionRef, context.RequestAborted);
        return Results.Ok(new ApiResponse<AdminOrderDetailResponse>(result));
    }

    private static async Task<IResult> CreateShipmentAsync(
        string code, HttpContext context, JsonElement? body, AdminOrderShipmentService service)
    {
        SetNoStore(context);

        // Kiểm body trước khi gọi GHN. Kích thước sai không tốn một lời gọi mạng.
        var failures = new List<ValidationFailure>();
        var parcel = CreateShipmentBody.Read(body, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var actorId = context.GetActor().UserId!;
        var result = await service.CreateAsync(code, actorId, parcel, context.RequestAborted);
        return Results.Ok(new ApiResponse<AdminOrderDetailResponse>(result));
    }

    private static async Task<IResult> CancelShipmentAsync(string code, HttpContext context, AdminOrderShipmentService service)
    {
        SetNoStore(context);

        var actorId = context.GetActor().UserId!;
        var result = await service.CancelAsync(code, actorId, context.RequestAborted);
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
