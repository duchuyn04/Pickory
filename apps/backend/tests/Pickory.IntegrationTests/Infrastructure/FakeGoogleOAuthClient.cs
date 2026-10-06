using Pickory.Modules.Identity.GoogleOAuth;

namespace Pickory.IntegrationTests.Infrastructure;

// Thay Google thật trong test. Test đặt thông tin người dùng mà Google sẽ trả về.
public sealed class FakeGoogleOAuthClient : IGoogleOAuthClient
{
    // Giá trị mặc định có email đã xác minh. Test chỉ đổi giá trị này khi cần kịch bản khác.
    public GoogleUserInfo UserInfo { get; set; } = new("google-sub-default", "google-default@pickory.local", EmailVerified: true, "Người dùng Google");

    // Ghi lại mỗi lần đổi mã. Test kiểm tra API gửi đúng `code_verifier` và `redirect_uri` cho Google.
    public List<(string Code, string CodeVerifier, string RedirectUri)> Exchanges { get; } = [];

    public Task<string> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        Exchanges.Add((code, codeVerifier, redirectUri));
        // Token giả chỉ đi tiếp vào `GetUserInfoAsync` của bản giả này. Không có request nào ra mạng.
        return Task.FromResult("fake-access-token");
    }

    public Task<GoogleUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        // Trả thông tin do test đặt. Test điều khiển `sub`, email và cờ xác minh mà API nhận.
        return Task.FromResult(UserInfo);
    }
}
