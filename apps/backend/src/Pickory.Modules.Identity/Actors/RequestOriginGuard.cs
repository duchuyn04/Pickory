using Microsoft.AspNetCore.Http;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.Actors;

// Chống CSRF (Cross-Site Request Forgery) cho request ghi. Chỉ trang web của Pickory được gửi request ghi.
public static class RequestOriginGuard
{
    private const string ForbiddenOriginCode = "FORBIDDEN_ORIGIN";

    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    };

    public static void EnsureTrustedOrigin(HttpRequest request, string webOrigin)
    {
        // Request đọc không đổi dữ liệu. Request đọc vì vậy không cần chống CSRF.
        if (!MutatingMethods.Contains(request.Method))
        {
            return;
        }

        // So sánh phần nguồn của URL. Dấu gạch chéo cuối trong cấu hình không làm lệch kết quả.
        var allowedOrigin = ReadOrigin(webOrigin)
            ?? throw new InvalidOperationException("WEB_ORIGIN phải là URL tuyệt đối.");

        string? requestOrigin = null;
        var originHeader = request.Headers.Origin.ToString();
        var refererHeader = request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(originHeader))
        {
            // `Origin` không đọc được là dấu hiệu request giả mạo. API từ chối ngay.
            requestOrigin = ReadOrigin(originHeader)
                ?? throw new AppException(ForbiddenOriginCode, 403, "Yêu cầu bị từ chối do Origin không hợp lệ.");
        }
        else if (!string.IsNullOrWhiteSpace(refererHeader))
        {
            // Một số trình duyệt không gửi `Origin` cho request ghi. `Referer` là nguồn thay thế.
            requestOrigin = ReadOrigin(refererHeader)
                ?? throw new AppException(ForbiddenOriginCode, 403, "Yêu cầu bị từ chối do Referer không hợp lệ.");
        }

        // Thiếu cả hai header thì API không chứng minh được request đến từ trang web của Pickory.
        if (requestOrigin is null || !string.Equals(requestOrigin, allowedOrigin, StringComparison.Ordinal))
        {
            throw new AppException(ForbiddenOriginCode, 403, "Yêu cầu bị từ chối do thiếu hoặc Origin/Referer không hợp lệ.");
        }
    }

    // Trả null khi chuỗi không phải URL tuyệt đối. Bản Node cũng báo lỗi khi `new URL()` không đọc được chuỗi.
    public static string? ReadOrigin(string value)
    {
        var trimmedValue = value.Trim();
        if (!Uri.TryCreate(trimmedValue, UriKind.Absolute, out var uri))
        {
            return null;
        }

        // .NET đọc đường dẫn như "/trang" thành URL `file` trên Linux. Bản Node coi chuỗi này là URL sai.
        if (uri.IsFile && !trimmedValue.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // URL có scheme khác HTTP hoặc HTTPS có nguồn "null" theo chuẩn URL. Nguồn này không khớp `WEB_ORIGIN`.
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return "null";
        }

        // `GetLeftPart` đổi tên máy sang chữ thường và bỏ cổng mặc định, giống nguồn của URL trong trình duyệt.
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
