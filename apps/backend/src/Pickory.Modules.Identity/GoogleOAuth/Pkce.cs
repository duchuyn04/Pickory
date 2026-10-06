using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Pickory.Modules.Identity.GoogleOAuth;

// Giá trị ngẫu nhiên cho một lần đăng nhập Google theo PKCE (Proof Key for Code Exchange).
public sealed record Pkce(string CodeVerifier, string CodeChallenge, string State)
{
    public static Pkce Generate()
    {
        // Verifier là bí mật chỉ máy chủ giữ. PKCE yêu cầu định dạng base64url.
        var codeVerifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        // Google chỉ nhận challenge. Kẻ chặn được redirect không có verifier, nên không đổi được code lấy token.
        var codeChallenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        // Callback phải mang đúng state này. State chứng minh luồng đăng nhập do chính trang web bắt đầu.
        var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        return new Pkce(codeVerifier, codeChallenge, state);
    }
}
