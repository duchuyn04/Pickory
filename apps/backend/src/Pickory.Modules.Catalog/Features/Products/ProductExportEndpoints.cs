using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Catalog.Features.Products;

// Xuất sản phẩm của nhân viên ra Excel. Route nằm trong Catalog vì chỉ Catalog đọc bảng sản phẩm.
internal static class ProductExportEndpoints
{
    private const string ExportPath = "/api/admin/exports/products";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Nhân viên cần đọc được sản phẩm và có quyền xuất. Thiếu một trong hai quyền thì trả 403.
        endpoints.MapGet(ExportPath, ExportAsync)
            .RequirePermission(Permissions.ProductsRead, Permissions.ReportsExport);
    }

    private static async Task<IResult> ExportAsync(HttpContext context, ProductExportService service)
    {
        // Phản hồi của route quản trị không cho trình duyệt hay proxy lưu bản sao, kể cả khi lỗi.
        context.Response.Headers.CacheControl = "no-store";

        // Kiểm toàn bộ query string trước khi chạm DB. Tham số lọc sai không tốn một truy vấn.
        var failures = new List<ValidationFailure>();
        var filter = ProductListQuery.Read(ExportQueryString.WithoutPaging(context.Request.Query), failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // Xuất mọi dòng khớp bộ lọc. Lỗi vượt giới hạn hoặc dựng file lỗi dừng ở đây và không trả file.
        var file = await service.ExportAsync(filter, context.RequestAborted);
        // Trả file kèm header tải xuống. Hàm `Xlsx` kiểm tên file an toàn trước khi ghi header.
        return ExportResults.Xlsx(file.Content, file.FileName);
    }
}
