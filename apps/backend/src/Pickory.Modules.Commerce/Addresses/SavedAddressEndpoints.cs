using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Commerce.Addresses;

// Bốn route sổ địa chỉ, chỉ dành cho khách đã đăng nhập.
// Route ghi đi qua kiểm `Origin` của actor middleware, như mọi route ghi khác.
internal static class SavedAddressEndpoints
{
    private const string AddressesPath = "/api/me/addresses";
    private const string AddressPath = "/api/me/addresses/{id}";
    private const string StaffForbiddenMessage = "Sổ địa chỉ chỉ dành cho tài khoản khách hàng.";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Chưa đăng nhập nhận 401 trước khi handler chạy. Loại tài khoản được kiểm trong handler.
        endpoints.MapGet(AddressesPath, ListAsync).RequireAuth();
        endpoints.MapPost(AddressesPath, CreateAsync).RequireAuth();
        endpoints.MapPatch(AddressPath, UpdateAsync).RequireAuth();
        endpoints.MapDelete(AddressPath, DeleteAsync).RequireAuth();
    }

    private static async Task<IResult> ListAsync(HttpContext context, SavedAddressService service)
    {
        // Địa chỉ nhà và số điện thoại là dữ liệu cá nhân, nên không cho trình duyệt hay proxy lưu bản sao.
        context.Response.Headers.CacheControl = "no-store";
        var addresses = await service.ListAsync(RequireCustomerId(context), context.RequestAborted);
        return Results.Ok(new ApiResponse<IReadOnlyList<SavedAddressResponse>>(addresses));
    }

    private static async Task<IResult> CreateAsync(HttpContext context, JsonElement? body, SavedAddressService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        var userId = RequireCustomerId(context);

        // Kiểm body trước mọi truy cập DB. Body sai không tra danh mục và không lấy khóa.
        var input = SavedAddressRequests.ReadCreate(body);
        var created = await service.CreateAsync(userId, input, context.RequestAborted);
        return Results.Json(new ApiResponse<SavedAddressResponse>(created), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateAsync(
        string id, HttpContext context, JsonElement? body, SavedAddressService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        var userId = RequireCustomerId(context);

        var patch = SavedAddressRequests.ReadPatch(body);
        var updated = await service.UpdateAsync(userId, id, patch, context.RequestAborted);
        return Results.Ok(new ApiResponse<SavedAddressResponse>(updated));
    }

    private static async Task<IResult> DeleteAsync(string id, HttpContext context, SavedAddressService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        await service.DeleteAsync(RequireCustomerId(context), id, context.RequestAborted);
        return Results.NoContent();
    }

    private static string RequireCustomerId(HttpContext context)
    {
        // `RequireAuth` đã chặn khách chưa đăng nhập. Nhân viên có phiên hợp lệ nhưng không có sổ địa chỉ mua hàng.
        var actor = context.GetActor();
        if (actor.Kind != ActorKind.Customer)
        {
            throw new AppException("FORBIDDEN", 403, StaffForbiddenMessage);
        }

        return actor.UserId!;
    }
}
