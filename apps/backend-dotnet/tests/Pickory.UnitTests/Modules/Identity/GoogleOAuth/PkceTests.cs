using System.Security.Cryptography;
using System.Text;
using Pickory.Modules.Identity.GoogleOAuth;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.GoogleOAuth;

// Kiểm tra giá trị PKCE (Proof Key for Code Exchange) và state cho đăng nhập Google.
public class PkceTests
{
    [Fact]
    // Challenge là SHA-256 base64url của verifier. Google chỉ đổi code khi verifier khớp challenge.
    public void Generate_ChallengeIsBase64UrlSha256OfVerifier()
    {
        var pkce = Pkce.Generate();

        // 32 byte ngẫu nhiên mã hóa base64url không đệm cho 43 ký tự.
        Assert.Matches("^[A-Za-z0-9_-]{43}$", pkce.CodeVerifier);
        // Tính challenge độc lập theo chuẩn S256. Kết quả phải khớp giá trị `Pkce` tạo ra.
        var expectedChallenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(pkce.CodeVerifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expectedChallenge, pkce.CodeChallenge);
        // State là 16 byte ngẫu nhiên dạng hex. Kẻ tấn công không đoán trước được state của luồng đăng nhập.
        Assert.Matches("^[0-9a-f]{32}$", pkce.State);
    }

    [Fact]
    // Mỗi lần đăng nhập dùng verifier và state mới. Giá trị cũ bị lộ không dùng lại được.
    public void Generate_ReturnsFreshValuesEachTime()
    {
        var first = Pkce.Generate();
        var second = Pkce.Generate();

        // Hai lần gọi liên tiếp cho hai giá trị khác nhau.
        Assert.NotEqual(first.CodeVerifier, second.CodeVerifier);
        Assert.NotEqual(first.State, second.State);
    }
}
