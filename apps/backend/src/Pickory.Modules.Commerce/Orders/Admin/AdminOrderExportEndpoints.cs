using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Xuất đơn hàng của nhân viên ra Excel. Route nằm trong Commerce vì chỉ Commerce đọc bảng đơn hàng.
internal static class AdminOrderExportEndpoints
{
    private const string ExportPath = "/api/admin/exports/orders";

    // Hai tham số phân trang của route danh sách. Route xuất bỏ qua chúng và luôn trả mọi đơn khớp bộ lọc.
    private static readonly string[] PagingParameters = ["page", "limit"];

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Nhân viên cần đọc được danh sách đơn và có quyền xuất. Thiếu một trong hai quyền thì trả 403.
        endpoints.MapGet(ExportPath, ExportAsync)
            .RequirePermission(Permissions.OrdersRead, Permissions.ReportsExport);
    }

    private static async Task<IResult> ExportAsync(HttpContext context, AdminOrderExportService service)
    {
        // File chứa tên, số điện thoại và địa chỉ của khách, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";

        // Kiểm toàn bộ query string trước khi chạm DB. Tham số lọc sai không tốn một truy vấn và không ghi nhật ký.
        var failures = new List<ValidationFailure>();
        var filter = AdminOrderListQuery.Read(WithoutPaging(context.Request.Query), failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // `RequirePermission` chỉ cho nhân viên đi qua, nên actor luôn có `UserId`. Nhật ký cần id thật.
        var actorId = context.GetActor().UserId!;
        var file = await service.ExportAsync(filter, actorId, context.RequestAborted);
        return ExportResults.Xlsx(file.Content, file.FileName);
    }

    // Bỏ `page` và `limit` trước khi đọc tham số. Giá trị sai của hai tham số này không gây lỗi 400, theo contract.
    private static QueryCollection WithoutPaging(IQueryCollection query)
    {
        var remaining = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in query)
        {
            if (!PagingParameters.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                remaining[name] = values;
            }
        }

        return new QueryCollection(remaining);
    }
}
