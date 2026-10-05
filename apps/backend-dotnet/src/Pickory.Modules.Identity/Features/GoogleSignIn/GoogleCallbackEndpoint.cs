using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Pickory.Modules.Identity.Contracts.Actors;
using Pickory.Modules.Identity.Sessions;
using Pickory.Modules.Identity.Users;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.GoogleSignIn;

// `GET /api/auth/google/callback` nhận người dùng Google quay về, kiểm tra state rồi cấp phiên.
public static class GoogleCallbackEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/google/callback", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        GoogleCallbackHandler handler,
        AppConfig config,
        IHostEnvironment environment)
    {
        var code = context.Request.Query["code"].FirstOrDefault();
        var returnedState = context.Request.Query["state"].FirstOrDefault();
        // Thiếu code hoặc state nghĩa là Google không trả kết quả hợp lệ, ví dụ người dùng từ chối cấp quyền.
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(returnedState))
        {
            throw new AppException("INVALID_REQUEST", 400, "Thiếu mã code hoặc state từ Google.");
        }

        var savedState = OAuthStateCookie.Read(context.Request);
        // Xóa cookie trước khi kiểm tra. State chỉ dùng một lần, kể cả khi lần dùng này thất bại.
        OAuthStateCookie.Delete(context.Response);

        var (user, session) = await handler.SignInAsync(
            code,
            returnedState,
            savedState,
            GoogleRequestMode.CallbackUri(config.ApiBaseUrl),
            context.GetActor().AnonymousId,
            context.RequestAborted);

        // Cookie có cùng hạn 30 ngày với phiên khách trong DB.
        SessionCookies.AppendSessionCookie(context.Response, session.RawToken, SessionCookies.CustomerSessionLifetime, environment.IsProduction());

        // Test và client tự điều hướng nhận hồ sơ dạng JSON. Trình duyệt thường được chuyển về trang web.
        if (GoogleRequestMode.WantsJson(context.Request))
        {
            return Results.Ok(new ApiResponse<UserResponse<UserSummaryResponse>>(new(user)));
        }

        // Kiểm tra lại `returnTo` vì giá trị này đi qua cookie. Chỉ đường dẫn nội bộ được ghép vào `WEB_ORIGIN`.
        var returnTo = OAuthStateCookie.KeepInternalPath(savedState?.ReturnTo);
        return Results.Redirect(returnTo is null ? config.WebOrigin : config.WebOrigin + returnTo);
    }
}
