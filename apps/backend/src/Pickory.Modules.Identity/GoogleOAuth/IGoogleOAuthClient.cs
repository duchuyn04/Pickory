namespace Pickory.Modules.Identity.GoogleOAuth;

// Tách lời gọi Google ra giao diện. Test thay bằng bản giả và không gọi mạng thật.
public interface IGoogleOAuthClient
{
    Task<string> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken);

    Task<GoogleUserInfo> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken);
}
