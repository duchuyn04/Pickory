using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Payments.Admin;

// Danh sách và đối soát tay giao dịch SePay của nhân viên, mỗi route cần quyền riêng.
internal static class AdminPaymentEndpoints
{
    private const string TransactionsPath = "/api/admin/payments/transactions";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(TransactionsPath, GetListAsync).RequirePermission(Permissions.PaymentsRead);

        // Gắn đơn hoặc bỏ qua là thay đổi một phần của giao dịch, nên dùng PATCH. Quyền thuộc nhóm đối soát thanh toán.
        endpoints.MapPatch($"{TransactionsPath}/{{id}}", UpdateAsync).RequirePermission(Permissions.PaymentsReconcile);
    }

    private static async Task<IResult> UpdateAsync(
        string id, HttpContext context, JsonElement? body, AdminPaymentReconciliationService service)
    {
        context.Response.Headers.CacheControl = "no-store";

        // Kiểm body trước khi mở transaction. Body sai không khóa dòng và không ghi audit.
        var failures = new List<ValidationFailure>();
        var request = UpdatePaymentTransactionRequest.Read(body, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // `RequirePermission` chỉ cho nhân viên đi qua, nên actor luôn có `UserId`. Lịch sử và audit cần id thật.
        var actorId = context.GetActor().UserId!;
        var result = request.OrderCode is { } orderCode
            ? await service.AttachAsync(id, orderCode, actorId, context.RequestAborted)
            : await service.IgnoreAsync(id, actorId, context.RequestAborted);
        return Results.Ok(new ApiResponse<AdminPaymentTransactionResponse>(result));
    }

    private static async Task<IResult> GetListAsync(HttpContext context, AdminPaymentTransactionService service)
    {
        // Nội dung chuyển khoản có thể chứa tên người chuyển, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";

        // Kiểm toàn bộ query string trước khi chạm DB. Tham số sai không tốn một truy vấn.
        var failures = new List<ValidationFailure>();
        var filter = AdminPaymentTransactionListQuery.Read(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var result = await service.GetListAsync(filter, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<AdminPaymentTransactionResponse>>(result));
    }
}
