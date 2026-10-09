using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.Login;

// `POST /api/admin/auth/login` đăng nhập nhân viên. Phiên có hạn 12 giờ.
public static class StaffLoginEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/auth/login", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        LoginRequest? request,
        IValidator<LoginRequest> validator,
        LoginHandler handler,
        IHostEnvironment environment)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);

        // Gửi IP cho bộ đếm đăng nhập sai. Nhân viên không có giỏ hàng, nên route không gửi mã ẩn danh.
        var (user, session) = await handler.LoginStaffAsync(validRequest, context.GetClientIp(), context.RequestAborted);

        // Cookie có cùng hạn 12 giờ với phiên nhân viên trong DB.
        SessionCookies.AppendSessionCookie(context.Response, session.RawToken, SessionCookies.StaffSessionLifetime, environment.IsProduction());
        return Results.Ok(new ApiResponse<UserResponse<UserSummaryResponse>>(new(user)));
    }
}
