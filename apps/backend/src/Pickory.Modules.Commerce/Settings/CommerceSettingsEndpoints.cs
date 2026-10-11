using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Settings;

// Xem và sửa ngưỡng miễn phí vận chuyển, hạn mức COD, giới hạn giỏ hàng và tài khoản nhận tiền SePay.
internal static class CommerceSettingsEndpoints
{
    private const string SettingsPath = "/api/admin/commerce/settings";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Cả đọc và sửa cùng cần `commerce-settings:manage`. Mặc định chỉ admin có quyền này.
        endpoints.MapGet(SettingsPath, GetAsync).RequirePermission(Permissions.CommerceSettingsManage);
        endpoints.MapPut(SettingsPath, UpdateAsync).RequirePermission(Permissions.CommerceSettingsManage);
    }

    private static async Task<IResult> GetAsync(CommerceSettingsStore store, CancellationToken cancellationToken)
    {
        var row = await store.GetAsync(cancellationToken);
        var view = row is null ? CommerceSettingsView.FromDefaults() : CommerceSettingsView.From(row);
        return Results.Ok(new ApiResponse<CommerceSettingsView>(view));
    }

    private static async Task<IResult> UpdateAsync(HttpContext context, JsonElement? body, CommerceSettingsStore store)
    {
        // Kiểm toàn bộ body trước khi mở transaction. Body sai không ghi cấu hình và không ghi audit.
        var failures = new List<ValidationFailure>();
        var input = UpdateCommerceSettingsRequest.Read(body, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        // `RequirePermission` chỉ cho nhân viên đi qua, nên actor luôn có `UserId`. Audit cần id thật của người sửa.
        var actorId = context.GetActor().UserId!;
        var saved = await store.SaveAsync(input, actorId, context.RequestAborted);
        return Results.Ok(new ApiResponse<CommerceSettingsView>(CommerceSettingsView.From(saved)));
    }
}
