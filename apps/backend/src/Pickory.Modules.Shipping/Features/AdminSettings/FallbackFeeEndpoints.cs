using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Shipping.FallbackFees;
using Pickory.Shared.Http;

namespace Pickory.Modules.Shipping.Features.AdminSettings;

// Xem và sửa bảng phí dự phòng dùng khi GHN không phản hồi lúc báo phí.
internal static class FallbackFeeEndpoints
{
    private const string FallbackFeePath = "/api/admin/shipping/fallback-fee";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Cả đọc và sửa cùng cần `shipping-settings:manage`, giống route cấu hình lấy hàng.
        endpoints.MapGet(FallbackFeePath, GetAsync).RequirePermission(Permissions.ShippingSettingsManage);
        endpoints.MapPut(FallbackFeePath, UpdateAsync).RequirePermission(Permissions.ShippingSettingsManage);
    }

    private static async Task<IResult> GetAsync(FallbackFeeStore store, CancellationToken cancellationToken)
    {
        var row = await store.GetAsync(cancellationToken);
        // Chưa lưu lần nào thì trả giá trị mặc định (tắt, chưa có giá), để UI không phải kiểm `null` cho cả đối tượng.
        var view = row is null ? FallbackFeeView.Default : FallbackFeeView.From(row);
        return Results.Ok(new ApiResponse<FallbackFeeView>(view));
    }

    private static async Task<IResult> UpdateAsync(HttpContext context, JsonElement? body, FallbackFeeStore store)
    {
        // Kiểm toàn bộ body trước khi mở transaction. Body sai không ghi cấu hình và không ghi audit.
        var input = UpdateFallbackFeeRequest.Parse(body);
        // `RequirePermission` chỉ cho nhân viên đi qua, nên actor luôn có `UserId`. Audit cần id thật của người sửa.
        var actorId = context.GetActor().UserId!;
        var saved = await store.SaveAsync(input, actorId, context.RequestAborted);
        return Results.Ok(new ApiResponse<FallbackFeeView>(FallbackFeeView.From(saved)));
    }
}
