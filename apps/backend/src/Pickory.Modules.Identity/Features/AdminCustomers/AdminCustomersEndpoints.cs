using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.AdminCustomers;

// Route tra cứu, khóa và mở khóa khách hàng.
public static class AdminCustomersEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Nhân viên bán hàng và CSKH được xem khách. Khóa và mở khóa cần quyền quản lý riêng.
        endpoints.MapGet("/api/admin/customers", ListAsync).RequirePermission(Permissions.CustomersRead);
        endpoints.MapGet("/api/admin/customers/{id}", GetAsync).RequirePermission(Permissions.CustomersRead);
        endpoints.MapPost("/api/admin/customers/{id}/lock", LockAsync).RequirePermission(Permissions.CustomersManage);
        endpoints.MapPost("/api/admin/customers/{id}/unlock", UnlockAsync).RequirePermission(Permissions.CustomersManage);
    }

    private static async Task<IResult> ListAsync(HttpContext context, CustomerManagementHandler handler)
    {
        // Kiểm tra tham số phân trang trước khi truy vấn. Giá trị sai nhận lỗi `VALIDATION_ERROR`.
        var failures = new List<ValidationFailure>();
        var page = PageQuery.Read(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var result = await handler.ListAsync(context.Request.Query["search"].FirstOrDefault(), page, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<CustomerResponse>>(result));
    }

    private static async Task<IResult> GetAsync(HttpContext context, string id, CustomerManagementHandler handler)
    {
        return Results.Ok(new ApiResponse<CustomerResponse>(await handler.GetAsync(id, context.RequestAborted)));
    }

    private static async Task<IResult> LockAsync(HttpContext context, string id, CustomerManagementHandler handler)
    {
        // Nhật ký ghi actor là người thực hiện thao tác khóa.
        await handler.LockAsync(context.GetActor(), id, context.RequestAborted);
        return Results.Ok(new ApiResponse<AccountActionResponse>(new AccountActionResponse(Success: true, "Đã khóa tài khoản khách hàng.")));
    }

    private static async Task<IResult> UnlockAsync(HttpContext context, string id, CustomerManagementHandler handler)
    {
        await handler.UnlockAsync(context.GetActor(), id, context.RequestAborted);
        return Results.Ok(new ApiResponse<AccountActionResponse>(new AccountActionResponse(Success: true, "Đã mở khóa tài khoản khách hàng.")));
    }
}
