using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Pickory.Shared.Configuration;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Identity.GoogleOAuth;

// Gọi endpoint OAuth của Google. Lỗi chi tiết của Google không được đưa ra ngoài.
public sealed class GoogleOAuthClient(HttpClient httpClient, AppConfig config) : IGoogleOAuthClient
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

    public async Task<string> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = config.GoogleClientId,
            ["client_secret"] = config.GoogleClientSecret,
            // Google từ chối khi `redirect_uri` khác giá trị đã dùng lúc chuyển hướng.
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            // Google băm lại verifier và so với challenge ban đầu. Chỉ bên tạo verifier mới đổi được code.
            ["code_verifier"] = codeVerifier,
        });

        using var response = await httpClient.PostAsync(TokenEndpoint, form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AppException("GOOGLE_AUTH_FAILED", 400, "Không thể đổi mã xác thực với Google.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken);
        if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
        {
            throw new AppException("GOOGLE_AUTH_FAILED", 400, "Phản hồi từ Google thiếu access token.");
        }

        return tokenResponse.AccessToken;
    }

    public async Task<GoogleUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        // Access token đi trong header `Authorization`, không đi trong URL. URL có thể bị ghi vào log.
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var userInfo = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<GoogleUserInfoResponse>(cancellationToken)
            : null;
        // Thiếu `sub` hoặc email thì API không xác định được tài khoản. Lỗi của Google không được đưa ra ngoài.
        if (userInfo?.Sub is null || userInfo.Email is null)
        {
            throw new AppException("GOOGLE_AUTH_FAILED", 400, "Không thể lấy thông tin người dùng từ Google.");
        }

        return new GoogleUserInfo(userInfo.Sub, userInfo.Email, userInfo.EmailVerified, userInfo.Name);
    }

    // Google trả tên trường dạng snake_case. Thuộc tính đặt tên rõ để không phụ thuộc cấu hình JSON chung.
    private sealed record GoogleTokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record GoogleUserInfoResponse(
        [property: JsonPropertyName("sub")] string? Sub,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("email_verified")] bool EmailVerified,
        [property: JsonPropertyName("name")] string? Name);
}
