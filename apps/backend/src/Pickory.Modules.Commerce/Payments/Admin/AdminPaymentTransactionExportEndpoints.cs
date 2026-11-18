using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Commerce.Payments.Admin;

// Xuất giao dịch SePay của nhân viên ra Excel. Route nằm trong Commerce vì chỉ Commerce đọc bảng giao dịch.
internal static class AdminPaymentTransactionExportEndpoints
{
    private const string ExportPath = "/api/admin/exports/payment-transactions";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Nhân viên cần đọc được giao dịch và có quyền xuất. Thiếu một trong hai quyền thì trả 403.
        endpoints.MapGet(ExportPath, ExportAsync)
            .RequirePermission(Permissions.PaymentsRead, Permissions.ReportsExport);
    }

    private static async Task<IResult> ExportAsync(HttpContext context, AdminPaymentTransactionExportService service)
    {
        // Nội dung chuyển khoản có thể chứa tên người chuyển, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";

        // Kiểm toàn bộ query string trước khi chạm DB. Tham số lọc sai không tốn một truy vấn và không ghi nhật ký.
        var failures = new List<ValidationFailure>();
        var filter = AdminPaymentTransactionListQuery.Read(ExportQueryString.WithoutPaging(context.Request.Query), failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // `RequirePermission` chỉ cho nhân viên đi qua, nên actor luôn có `UserId`. Nhật ký cần id thật.
        var actorId = context.GetActor().UserId!;
        var file = await service.ExportAsync(filter, actorId, context.RequestAborted);
        return ExportResults.Xlsx(file.Content, file.FileName);
    }
}
