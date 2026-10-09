using System.Net.Http.Json;
using System.Text.Json;

namespace Pickory.IntegrationTests.Api.Identity;

// Công cụ đọc và gửi cookie cho test actor. Test kiểm tra từng header `Set-Cookie` như trình duyệt nhận được.
internal static class ActorHttp
{
    // Tạo request đọc có thể kèm cookie. Test gửi lại cookie giống trình duyệt để giữ cùng phiên.
    public static HttpRequestMessage CreateRequest(HttpMethod method, string path, string? cookieHeader = null)
    {
        var request = new HttpRequestMessage(method, path);
        // Request không có cookie mô phỏng khách mới vào trang lần đầu.
        if (cookieHeader is not null)
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        return request;
    }

    // Request ghi từ trình duyệt luôn có `Origin` của web. Thiếu header này thì actor middleware trả 403.
    public static HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, object? body, string? cookieHeader = null)
    {
        var request = CreateRequest(method, path, cookieHeader);
        request.Headers.Add("Origin", "http://localhost:5173");
        // Một số endpoint ghi không cần body, ví dụ đăng xuất.
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    // Đọc token phiên từ đúng một cookie `pk_session`. Hai cookie cùng tên nghĩa là API đặt phiên sai.
    public static string ReadSessionToken(HttpResponseMessage response)
    {
        return ReadCookieValue(ReadSetCookies(response, "pk_session").Single());
    }

    // Lọc header `Set-Cookie` theo tên cookie. Test cần đếm số lần API đặt hoặc xóa một cookie.
    public static IReadOnlyList<string> ReadSetCookies(HttpResponseMessage response, string cookieName)
    {
        // Phản hồi không đặt cookie thì trả danh sách rỗng. Test dùng danh sách rỗng để khẳng định API không cấp cookie.
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return [];
        }

        // So khớp cả dấu `=` sau tên. Cookie có tên bắt đầu giống nhau không lọt vào kết quả.
        return setCookies.Where(value => value.StartsWith(cookieName + "=", StringComparison.Ordinal)).ToList();
    }

    // Lấy giá trị cookie từ một header `Set-Cookie`. Test bỏ qua các thuộc tính sau dấu `;`.
    public static string ReadCookieValue(string setCookie)
    {
        var nameAndValue = setCookie.Split(';', 2)[0];
        return nameAndValue[(nameAndValue.IndexOf('=') + 1)..];
    }

    // Đọc body JSON của phản hồi. Test kiểm tra từng trường của hợp đồng API.
    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
