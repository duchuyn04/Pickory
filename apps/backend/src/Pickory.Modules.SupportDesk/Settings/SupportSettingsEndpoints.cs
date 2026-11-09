using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.SupportDesk.Presence;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.SupportDesk.Settings;

// Hai route cấu hình CSKH của trang quản trị. Cả hai cần `support-settings:manage`.
internal static class SupportSettingsEndpoints
{
    private const string SettingsPath = "/api/admin/support/settings";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Đọc cũng cần quyền quản lý, vì chỉ người sửa cấu hình mới có màn hình này.
        endpoints.MapGet(SettingsPath, GetAsync)
            .RequirePermission(Permissions.SupportSettingsManage);
        endpoints.MapPut(SettingsPath, UpdateAsync)
            .RequirePermission(Permissions.SupportSettingsManage);
    }

    internal static async Task<IResult> GetAsync(SupportSettingsService service, HttpContext context)
    {
        var settings = await service.GetAsync(context.RequestAborted);
        return Results.Ok(new ApiResponse<SupportSettingsResponse>(settings));
    }

    internal static async Task<IResult> UpdateAsync(
        HttpContext context,
        UpdateSupportSettingsRequest? request,
        IValidator<UpdateSupportSettingsRequest> validator,
        SupportSettingsService service)
    {
        // Kiểm body trước khi mở transaction để body sai không chạm DB và không ghi audit.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var staffId = SupportStaff.RequireStaffId(context);
        var saved = await service.UpdateAsync(staffId, validRequest.ToValues(), context.RequestAborted);
        return Results.Ok(new ApiResponse<SupportSettingsResponse>(saved));
    }
}
