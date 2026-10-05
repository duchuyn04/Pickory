using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.UpdateProfile;

// `PATCH /api/me` sửa tên và số điện thoại của người dùng đang đăng nhập.
public static class UpdateProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Chỉ người đã đăng nhập mới có hồ sơ để sửa.
        endpoints.MapPatch("/api/me", HandleAsync).RequireAuth();
    }

    private static async Task<IResult> HandleAsync(HttpContext context, JsonElement? body, UpdateProfileHandler handler)
    {
        // Đọc body thủ công. Trường bị thiếu được giữ nguyên, còn trường `null` bị xóa.
        var request = UpdateProfileRequest.FromJson(body);
        // `RequireAuth` đã chặn khách, nên actor luôn có `UserId` ở bước này.
        var user = await handler.UpdateAsync(context.GetActor().UserId!, request, context.RequestAborted);
        return Results.Ok(new ApiResponse<UserResponse<UserProfileResponse>>(new(user)));
    }
}
