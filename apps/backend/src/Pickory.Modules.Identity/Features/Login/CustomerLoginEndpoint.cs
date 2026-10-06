using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.Login;

// `POST /api/auth/login` đăng nhập khách bằng email và mật khẩu.
public static class CustomerLoginEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", HandleAsync);
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

        // Gửi IP cho bộ đếm đăng nhập sai. Gửi mã ẩn danh để giỏ hàng của khách được gộp vào tài khoản.
        var (user, session) = await handler.LoginCustomerAsync(
            validRequest, context.GetClientIp(), context.GetActor().AnonymousId, context.RequestAborted);

        // Cookie có cùng hạn 30 ngày với phiên khách trong DB.
        SessionCookies.AppendSessionCookie(context.Response, session.RawToken, SessionCookies.CustomerSessionLifetime, environment.IsProduction());
        return Results.Ok(new ApiResponse<UserResponse<UserProfileResponse>>(new(user)));
    }
}
