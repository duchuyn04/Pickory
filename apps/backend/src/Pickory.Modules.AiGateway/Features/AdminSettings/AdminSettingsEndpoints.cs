using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.AiGateway.Settings;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Http;

namespace Pickory.Modules.AiGateway.Features.AdminSettings;

// Xem và sửa ngân sách AI cùng hạn mức chat theo ngày.
public static class AdminSettingsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Người xem chi phí cũng cần thấy cấu hình đang áp dụng để đọc đúng báo cáo.
        endpoints.MapGet("/api/admin/ai/settings", GetAsync).RequirePermission(Permissions.AiUsageRead);
        // Chỉ người có `ai-settings:manage` được sửa. Mặc định chỉ admin có quyền này.
        endpoints.MapPut("/api/admin/ai/settings", UpdateAsync).RequirePermission(Permissions.AiSettingsManage);
    }

    private static async Task<IResult> GetAsync(HttpContext context, AiSettingsStore store)
    {
        var settings = await store.GetAsync(context.RequestAborted);
        return Results.Ok(new ApiResponse<AiSettings>(settings));
    }

    private static async Task<IResult> UpdateAsync(HttpContext context, JsonElement? body, AiSettingsStore store)
    {
        var settings = UpdateSettingsRequest.FromJson(body);
        // `RequirePermission` chỉ cho nhân viên đi qua, nên actor luôn có `UserId`. Nhật ký cần id thật của người sửa.
        var actorId = context.GetActor().UserId!;
        var updated = await store.UpdateAsync(settings, actorId, context.RequestAborted);
        return Results.Ok(new ApiResponse<AiSettings>(updated));
    }
}
