using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Pickory.Modules.Identity.GoogleOAuth;
using Pickory.Shared.Configuration;
using Pickory.Shared.Http;

namespace Pickory.Modules.Identity.Features.GoogleSignIn;

// `GET /api/auth/google/start` bắt đầu đăng nhập Google và chuyển trình duyệt sang trang đồng ý của Google.
public static class GoogleStartEndpoint
{
    private const string GoogleAuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/google/start", Handle);
    }

    private static IResult Handle(HttpContext context, AppConfig config, IHostEnvironment environment)
    {
        // Mỗi lần đăng nhập dùng state và verifier mới. Giá trị cũ bị lộ không dùng lại được.
        var pkce = Pkce.Generate();
        // Chỉ giữ đường dẫn nội bộ. Kẻ xấu không dùng được `returnTo` để chuyển người dùng sang trang ngoài.
        var returnTo = OAuthStateCookie.KeepInternalPath(context.Request.Query["returnTo"].FirstOrDefault());

        // Callback đối chiếu state và verifier trong cookie `HttpOnly`. Script trên trang không đọc được hai giá trị này.
        new OAuthStateCookie(pkce.State, pkce.CodeVerifier, returnTo).Append(context.Response, environment.IsProduction());

        var authUrl = QueryHelpers.AddQueryString(GoogleAuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["client_id"] = config.GoogleClientId,
            ["redirect_uri"] = GoogleRequestMode.CallbackUri(config.ApiBaseUrl),
            // Luồng Authorization Code: Google trả code, máy chủ đổi code lấy token. Token không đi qua trình duyệt.
            ["response_type"] = "code",
            // Chỉ xin quyền tối thiểu gồm định danh, email và tên.
            ["scope"] = "openid email profile",
            ["state"] = pkce.State,
            ["code_challenge"] = pkce.CodeChallenge,
            // S256 nghĩa là challenge là SHA-256 của verifier. Cách này an toàn hơn kiểu `plain`.
            ["code_challenge_method"] = "S256",
        });

        // Test và client tự điều hướng nhận URL dạng JSON. Trình duyệt thường nhận chuyển hướng 302.
        if (GoogleRequestMode.WantsJson(context.Request))
        {
            return Results.Ok(new ApiResponse<GoogleStartResponse>(new GoogleStartResponse(authUrl, pkce.State)));
        }

        return Results.Redirect(authUrl);
    }
}
