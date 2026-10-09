using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.AdminStaff;

// Route quản lý nhân viên: danh sách, mời, gán role, khóa và mở khóa.
public static class AdminStaffEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/staff", ListAsync).RequirePermission(Permissions.StaffRead);
        endpoints.MapPost("/api/admin/staff/invitations", InviteAsync).RequirePermission(Permissions.StaffManage);
        endpoints.MapPut("/api/admin/staff/{id}/roles", AssignRolesAsync).RequirePermission(Permissions.StaffManage);
        endpoints.MapPost("/api/admin/staff/{id}/lock", LockAsync).RequirePermission(Permissions.StaffManage);
        endpoints.MapPost("/api/admin/staff/{id}/unlock", UnlockAsync).RequirePermission(Permissions.StaffManage);
    }

    private static async Task<IResult> ListAsync(HttpContext context, StaffManagementHandler handler)
    {
        return Results.Ok(new ApiResponse<IReadOnlyList<StaffListItemResponse>>(await handler.ListAsync(context.RequestAborted)));
    }

    private static async Task<IResult> InviteAsync(
        HttpContext context,
        InviteStaffRequest? request,
        IValidator<InviteStaffRequest> validator,
        StaffManagementHandler handler)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        // Handler nhận actor để chặn người mời đưa người khác vào role mạnh hơn quyền của chính họ.
        var invited = await handler.InviteAsync(context.GetActor(), validRequest, context.RequestAborted);
        // Trả 201 vì route vừa tạo tài khoản nhân viên mới, giống bản Node.
        return Results.Json(new ApiResponse<InvitedStaffResponse>(invited), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> AssignRolesAsync(
        HttpContext context,
        string id,
        AssignRolesRequest? request,
        IValidator<AssignRolesRequest> validator,
        StaffManagementHandler handler)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        var result = await handler.AssignRolesAsync(context.GetActor(), id, validRequest.RoleCodes!, context.RequestAborted);
        return Results.Ok(new ApiResponse<AssignedRolesResponse>(result));
    }

    private static async Task<IResult> LockAsync(HttpContext context, string id, StaffManagementHandler handler)
    {
        await handler.LockAsync(context.GetActor(), id, context.RequestAborted);
        return Results.Ok(new ApiResponse<AccountActionResponse>(new AccountActionResponse(Success: true, "Đã khóa tài khoản nhân viên.")));
    }

    private static async Task<IResult> UnlockAsync(HttpContext context, string id, StaffManagementHandler handler)
    {
        await handler.UnlockAsync(context.GetActor(), id, context.RequestAborted);
        return Results.Ok(new ApiResponse<AccountActionResponse>(new AccountActionResponse(Success: true, "Đã mở khóa tài khoản nhân viên.")));
    }
}
