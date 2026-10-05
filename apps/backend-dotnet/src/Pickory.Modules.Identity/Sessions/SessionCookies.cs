using Microsoft.AspNetCore.Http;

namespace Pickory.Modules.Identity.Sessions;

// Tên, thời hạn và thuộc tính cookie giữ nguyên bản Node. Frontend và phiên cũ dựa vào các giá trị này.
public static class SessionCookies
{
    public const string SessionCookieName = "pk_session";
    public const string AnonymousCookieName = "pk_anon";

    public static readonly TimeSpan CustomerSessionLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan AnonymousIdLifetime = TimeSpan.FromDays(365);

    public static void AppendSessionCookie(HttpResponse response, string rawToken, TimeSpan lifetime, bool isProduction)
    {
        response.Cookies.Append(SessionCookieName, rawToken, CreateOptions(lifetime, isProduction));
    }

    public static void AppendAnonymousCookie(HttpResponse response, string anonymousId, bool isProduction)
    {
        response.Cookies.Append(AnonymousCookieName, anonymousId, CreateOptions(AnonymousIdLifetime, isProduction));
    }

    public static void DeleteSessionCookie(HttpResponse response)
    {
        // Bản Node xóa cookie không kèm `Secure`. Giữ cùng thuộc tính để trình duyệt xóa đúng cookie.
        response.Cookies.Delete(SessionCookieName, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }

    public static CookieOptions CreateOptions(TimeSpan lifetime, bool isProduction)
    {
        return new CookieOptions
        {
            // Script trên trang không đọc được cookie. Lỗi XSS vì vậy không lấy được token.
            HttpOnly = true,
            // Môi trường dev chạy HTTP. Chỉ production bắt buộc HTTPS.
            Secure = isProduction,
            // Trình duyệt không gửi cookie trong request ghi từ trang khác.
            SameSite = SameSiteMode.Lax,
            Path = "/",
            // Bản Node gửi cả `Max-Age` và `Expires`. Trình duyệt cũ chỉ hiểu `Expires`.
            MaxAge = lifetime,
            Expires = DateTimeOffset.UtcNow.Add(lifetime),
        };
    }
}
