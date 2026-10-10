using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;
using Pickory.Shared.Validation;

namespace Pickory.Modules.Identity.Features.Register;

// `POST /api/auth/registrations` tạo một bản đăng ký khách và đăng nhập luôn.
public static class RegisterEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/registrations", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        RegisterRequest? request,
        IValidator<RegisterRequest> validator,
        IdentityRateLimiters rateLimiters,
        RegisterHandler handler,
        IHostEnvironment environment)
    {
        // Kiểm tra body trước mọi bước khác. Dữ liệu sai nhận lỗi `VALIDATION_ERROR` và không chạm DB.
        var validRequest = await RequestValidation.ValidateAsync(validator, request, context.RequestAborted);

        // Bot điền ô ẩn nhận lỗi chung. Bước này chạy trước bộ đếm, nên bot không tiêu lượt đăng ký của IP.
        if (!string.IsNullOrWhiteSpace(validRequest.Website) || !string.IsNullOrWhiteSpace(validRequest.Honeypot))
        {
            throw new AppException("BAD_REQUEST", 400, "Yêu cầu không hợp lệ.");
        }

        // Chặn lần thứ 6 trong giờ trước khi băm mật khẩu. Máy chủ không tốn CPU Argon2 cho request bị chặn.
        rateLimiters.Register.Consume(context.GetClientIp());

        // Gửi mã ẩn danh. Giỏ hàng của khách vãng lai được gộp vào tài khoản mới.
        var (user, session) = await handler.RegisterAsync(validRequest, context.GetActor().AnonymousId, context.RequestAborted);

        // Cookie có cùng hạn 30 ngày với phiên khách trong DB.
        SessionCookies.AppendSessionCookie(context.Response, session.RawToken, SessionCookies.CustomerSessionLifetime, environment.IsProduction());
        // Trả 201 vì route vừa tạo tài khoản mới, giống bản Node.
        return Results.Json(new ApiResponse<UserResponse<UserProfileResponse>>(new(user)), statusCode: StatusCodes.Status201Created);
    }
}
