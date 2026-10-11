using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.Settings;
using Pickory.Shared.Configuration;
using Pickory.Shared.Http;

namespace Pickory.Modules.Shipping.Features.AdminSettings;

// Xem và sửa địa chỉ lấy hàng cùng gói dịch vụ mặc định của shop.
internal static class ShippingSettingsEndpoints
{
    private const string SettingsPath = "/api/admin/shipping/settings";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Cả đọc và sửa cùng cần `shipping-settings:manage`. Mặc định chỉ admin có quyền này.
        endpoints.MapGet(SettingsPath, GetAsync).RequirePermission(Permissions.ShippingSettingsManage);
        endpoints.MapPut(SettingsPath, UpdateAsync).RequirePermission(Permissions.ShippingSettingsManage);
    }

    private static async Task<IResult> GetAsync(ShippingSettingsStore store, AppConfig config, CancellationToken cancellationToken)
    {
        var row = await store.GetAsync(cancellationToken);
        return Results.Ok(new ApiResponse<ShippingSettingsResponse>(BuildResponse(row, config)));
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext context,
        JsonElement? body,
        ShippingSettingsValidator validator,
        ShippingSettingsStore store,
        AppConfig config)
    {
        // Kiểm toàn bộ body trước khi mở transaction. Body sai không ghi cấu hình và không ghi audit.
        var validated = await validator.ValidateAsync(body, context.RequestAborted);
        // `RequirePermission` chỉ cho nhân viên đi qua, nên actor luôn có `UserId`. Audit cần id thật của người sửa.
        var actorId = context.GetActor().UserId!;
        var saved = await store.SaveAsync(validated, actorId, context.RequestAborted);
        return Results.Ok(new ApiResponse<ShippingSettingsResponse>(BuildResponse(saved, config)));
    }

    private static ShippingSettingsResponse BuildResponse(ShippingSettings? row, AppConfig config)
    {
        // Thiếu `GHN_SHOP_ID` thì trả `null` để UI biết shop chưa nối với GHN, thay vì hiện chuỗi rỗng.
        var shopId = string.IsNullOrWhiteSpace(config.GhnShopId) ? null : config.GhnShopId;
        return new ShippingSettingsResponse(row is null ? null : ShippingSettingsView.From(row), shopId);
    }
}
