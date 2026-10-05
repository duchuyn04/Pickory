using System.Text.Json;

namespace Pickory.IntegrationTests.Api.Identity;

// Công cụ đọc và gửi cookie cho test actor. Test kiểm tra từng header `Set-Cookie` như trình duyệt nhận được.
internal static class ActorHttp
{
    public static HttpRequestMessage CreateRequest(HttpMethod method, string path, string? cookieHeader = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (cookieHeader is not null)
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        return request;
    }

    public static IReadOnlyList<string> ReadSetCookies(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return [];
        }

        return setCookies.Where(value => value.StartsWith(cookieName + "=", StringComparison.Ordinal)).ToList();
    }

    public static string ReadCookieValue(string setCookie)
    {
        var nameAndValue = setCookie.Split(';', 2)[0];
        return nameAndValue[(nameAndValue.IndexOf('=') + 1)..];
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
