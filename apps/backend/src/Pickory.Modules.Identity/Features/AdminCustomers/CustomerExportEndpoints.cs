using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Identity.Features.AdminCustomers;

// Xuất khách của nhân viên ra Excel. Route nằm trong Identity vì chỉ Identity đọc bảng người dùng.
internal static class CustomerExportEndpoints
{
    private const string ExportPath = "/api/admin/exports/customers";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Nhân viên cần đọc được khách và có quyền xuất. Thiếu một trong hai quyền thì trả 403.
        endpoints.MapGet(ExportPath, ExportAsync)
            .RequirePermission(Permissions.CustomersRead, Permissions.ReportsExport);
    }

    private static async Task<IResult> ExportAsync(HttpContext context, CustomerExportService service)
    {
        // File chứa email và số điện thoại của khách, nên không cho trình duyệt hay proxy lưu bản sao, kể cả khi lỗi.
        context.Response.Headers.CacheControl = "no-store";

        // Kiểm toàn bộ query string trước khi chạm DB. Tham số lọc sai không tốn một truy vấn và không ghi nhật ký.
        var failures = new List<ValidationFailure>();
        var filter = CustomerListQuery.Read(ExportQueryString.WithoutPaging(context.Request.Query), failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // `RequirePermission` chỉ cho nhân viên đi qua, nên actor luôn có `UserId`. Nhật ký cần mã thật.
        var actorId = context.GetActor().UserId!;

        // Xuất mọi dòng khớp bộ lọc. Lỗi vượt giới hạn hoặc dựng file lỗi dừng ở đây và không trả file.
        var file = await service.ExportAsync(filter, actorId, context.RequestAborted);

        // Trả file kèm header tải xuống. Hàm `Xlsx` kiểm tên file an toàn trước khi ghi header.
        return ExportResults.Xlsx(file.Content, file.FileName);
    }
}
