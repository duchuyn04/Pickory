using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Contracts.Authorization;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.ChangePassword;

// `POST /api/me/password` đổi mật khẩu của người dùng đang đăng nhập.
public static class ChangePasswordEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Chỉ người đã đăng nhập mới có mật khẩu để đổi.
        endpoints.MapPost("/api/me/password", HandleAsync).RequireAuth();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ChangePasswordRequest? request,
        IValidator<ChangePasswordRequest> validator,
        ChangePasswordHandler handler)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);
        // `RequireAuth` đã chặn khách, nên actor luôn có `UserId` ở bước này.
        await handler.ChangeAsync(context.GetActor().UserId!, validRequest, context.RequestAborted);

        // Mọi phiên đã bị xóa trong DB. Xóa cookie để trình duyệt về trạng thái đăng xuất.
        SessionCookies.DeleteSessionCookie(context.Response);
        return Results.Ok(new ApiResponse<AccountActionResponse>(
            new AccountActionResponse(Success: true, "Đổi mật khẩu thành công. Mọi phiên đăng nhập cũ đã được xóa.")));
    }
}
