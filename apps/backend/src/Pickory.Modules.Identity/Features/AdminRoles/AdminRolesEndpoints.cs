using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Roles;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.AdminRoles;

// Route quản lý role và danh mục quyền của trang quản trị.
public static class AdminRolesEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/permissions", ListPermissions).RequirePermission(Permissions.RolesManage);
        // Người quản lý nhân viên cũng cần xem role để chọn role khi mời hoặc gán. Route tự kiểm tra một trong hai quyền.
        endpoints.MapGet("/api/admin/roles", ListRolesAsync).RequireStaff();
        endpoints.MapPost("/api/admin/roles", CreateRoleAsync).RequirePermission(Permissions.RolesManage);
        endpoints.MapPatch("/api/admin/roles/{id}", UpdateRoleAsync).RequirePermission(Permissions.RolesManage);
        endpoints.MapDelete("/api/admin/roles/{id}", DeleteRoleAsync).RequirePermission(Permissions.RolesManage);
    }

    private static IResult ListPermissions()
    {
        return Results.Ok(new ApiResponse<IReadOnlyList<PermissionCatalogEntry>>(PermissionCatalog.Entries));
    }

    private static async Task<IResult> ListRolesAsync(HttpContext context, RoleManagementHandler handler)
    {
        var actor = context.GetActor();
        // Chỉ cần một trong hai quyền. `RequirePermission` đòi đủ mọi quyền, nên route không dùng bộ lọc đó.
        if (!actor.HasPermission(Permissions.RolesManage) && !actor.HasPermission(Permissions.StaffManage))
        {
            throw new AppException("FORBIDDEN", 403, "Bạn không có quyền xem danh sách vai trò.");
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<RoleResponse>>(await handler.ListAsync(context.RequestAborted)));
    }

    private static async Task<IResult> CreateRoleAsync(
        HttpContext context,
        CreateRoleRequest? request,
        IValidator<CreateRoleRequest> validator,
        RoleManagementHandler handler)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        // Handler nhận actor để chặn người gọi cấp quyền vượt quá quyền của chính họ.
        var role = await handler.CreateAsync(context.GetActor(), validRequest, context.RequestAborted);
        // Trả 201 vì route vừa tạo role mới, giống bản Node.
        return Results.Json(new ApiResponse<RoleResponse>(role), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateRoleAsync(
        HttpContext context,
        string id,
        UpdateRoleRequest? request,
        IValidator<UpdateRoleRequest> validator,
        RoleManagementHandler handler)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var role = await handler.UpdateAsync(context.GetActor(), id, validRequest, context.RequestAborted);
        return Results.Ok(new ApiResponse<RoleResponse>(role));
    }

    private static async Task<IResult> DeleteRoleAsync(HttpContext context, string id, RoleManagementHandler handler)
    {
        await handler.DeleteAsync(context.GetActor(), id, context.RequestAborted);
        return Results.Ok(new ApiResponse<SuccessResponse>(new SuccessResponse(Success: true)));
    }
}
