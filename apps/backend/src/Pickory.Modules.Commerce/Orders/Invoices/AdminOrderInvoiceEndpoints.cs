using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Orders.Invoices;

// Trả dữ liệu in hóa đơn cho nhân viên có quyền xem đơn hàng.
internal static class AdminOrderInvoiceEndpoints
{
    private const string InvoicesPath = "/api/admin/order-invoices";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Yêu cầu quyền đọc đơn hàng vì dữ liệu hóa đơn trùng phạm vi với chi tiết đơn.
        endpoints.MapGet(InvoicesPath, GetInvoicesAsync)
            .RequirePermission(Permissions.OrdersRead);
    }

    private static async Task<IResult> GetInvoicesAsync(HttpContext context, AdminOrderInvoiceService service)
    {
        // Đặt `no-store` trước khi kiểm tham số để cả phản hồi lỗi cũng không bị lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";

        // Kiểm tra toàn bộ tham số `codes` trước khi chạm DB để lỗi 400 không tốn truy vấn.
        var failures = new List<ValidationFailure>();
        var codes = OrderInvoiceCodesParser.Read(context.Request.Query, failures);
        // Ném ngoại lệ kiểm tra ngay để lớp trung gian trả 400 trước khi gọi dịch vụ.
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // Truyền `RequestAborted` để hủy truy vấn DB ngay khi người gọi đóng kết nối.
        var result = await service.GetInvoicesAsync(codes, context.RequestAborted);
        return Results.Ok(new ApiResponse<OrderInvoicesResponse>(result));
    }
}
