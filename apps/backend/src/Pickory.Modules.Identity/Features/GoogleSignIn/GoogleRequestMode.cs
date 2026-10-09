using Microsoft.AspNetCore.Http;

namespace Pickory.Modules.Identity.Features.GoogleSignIn;

public static class GoogleRequestMode
{
    // Chế độ JSON dành cho test và client muốn tự điều hướng thay vì nhận chuyển hướng.
    public static bool WantsJson(HttpRequest request)
    {
        return request.Query["json"] == "1" || request.Headers.Accept.ToString().Contains("application/json", StringComparison.Ordinal);
    }

    // Lấy địa chỉ callback từ cấu hình, không lấy từ header `Host`. Header này có thể bị giả mạo.
    public static string CallbackUri(string apiBaseUrl)
    {
        return $"{apiBaseUrl}/api/auth/google/callback";
    }
}
