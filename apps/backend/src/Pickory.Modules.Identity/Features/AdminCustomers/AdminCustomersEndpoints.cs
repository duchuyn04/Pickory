using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Features.AccountLock;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.AdminCustomers;

// Route tra cứu khách hàng và đổi trạng thái khóa của một khách bằng `PATCH` với trường `locked`.
public static class AdminCustomersEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Nhân viên bán hàng và CSKH được xem khách. Đổi trạng thái khóa cần quyền quản lý riêng.
        endpoints.MapGet("/api/admin/customers", ListAsync).RequirePermission(Permissions.CustomersRead);
        endpoints.MapGet("/api/admin/customers/{id}", GetAsync).RequirePermission(Permissions.CustomersRead);
        endpoints.MapPatch("/api/admin/customers/{id}", UpdateLockAsync).RequirePermission(Permissions.CustomersManage);
    }

    private static async Task<IResult> ListAsync(HttpContext context, CustomerManagementHandler handler)
    {
        // Kiểm tra mọi tham số của query string trước khi truy vấn. Giá trị sai nhận lỗi `VALIDATION_ERROR`.
        var failures = new List<ValidationFailure>();
        var query = CustomerListQuery.Read(context.Request.Query, failures);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var result = await handler.ListAsync(query, context.RequestAborted);
        return Results.Ok(new ApiResponse<PagedResponse<CustomerResponse>>(result));
    }

    private static async Task<IResult> GetAsync(HttpContext context, string id, CustomerManagementHandler handler)
    {
        return Results.Ok(new ApiResponse<CustomerResponse>(await handler.GetAsync(id, context.RequestAborted)));
    }

    private static async Task<IResult> UpdateLockAsync(HttpContext context, string id, JsonElement? body, CustomerManagementHandler handler)
    {
        // Đọc body trước khi chạm DB. Body thiếu `locked` hoặc sai kiểu nhận lỗi `VALIDATION_ERROR` và không đổi tài khoản.
        var locked = AccountLockRequest.ReadLocked(body);

        // Nhật ký ghi actor là người thực hiện thao tác. Mỗi giá trị `locked` đi qua đúng một hàm và đúng một dòng nhật ký.
        if (locked)
        {
            await handler.LockAsync(context.GetActor(), id, context.RequestAborted);
            return Results.Ok(new ApiResponse<AccountActionResponse>(new AccountActionResponse(Success: true, "Đã khóa tài khoản khách hàng.")));
        }

        await handler.UnlockAsync(context.GetActor(), id, context.RequestAborted);
        return Results.Ok(new ApiResponse<AccountActionResponse>(new AccountActionResponse(Success: true, "Đã mở khóa tài khoản khách hàng.")));
    }
}
