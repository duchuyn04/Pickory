using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Pickory.Modules.Identity.Sessions;

namespace Pickory.Modules.Identity.Features.GoogleSignIn;

// Cookie `pk_oauth_state` giữ state và verifier giữa hai bước đăng nhập Google. Dạng JSON giữ nguyên bản Node.
public sealed record OAuthStateCookie(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("codeVerifier")] string CodeVerifier,
    [property: JsonPropertyName("returnTo")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ReturnTo)
{
    public const string CookieName = "pk_oauth_state";

    // Cookie chỉ sống 10 phút, vừa đủ cho một lần đăng nhập.
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // Cookie dùng cùng thuộc tính `HttpOnly` và `SameSite=Lax` với cookie phiên.
    public void Append(HttpResponse response, bool isProduction)
    {
        response.Cookies.Append(CookieName, JsonSerializer.Serialize(this), SessionCookies.CreateOptions(Lifetime, isProduction));
    }

    // Trả null khi cookie thiếu hoặc hỏng. Callback khi đó từ chối vì state không khớp.
    public static OAuthStateCookie? Read(HttpRequest request)
    {
        var rawValue = request.Cookies[CookieName];
        if (string.IsNullOrEmpty(rawValue))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OAuthStateCookie>(rawValue);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // State chỉ dùng một lần. Callback xóa cookie để state cũ không bị phát lại.
    public static void Delete(HttpResponse response)
    {
        SessionCookies.DeleteCookie(response, CookieName);
    }

    // Chỉ nhận đường dẫn nội bộ. Chuỗi bắt đầu bằng "//" là địa chỉ ngoài, nên bị bỏ để chặn open redirect.
    public static string? KeepInternalPath(string? returnTo)
    {
        return returnTo is not null && returnTo.StartsWith('/') && !returnTo.StartsWith("//", StringComparison.Ordinal)
            ? returnTo
            : null;
    }
}
