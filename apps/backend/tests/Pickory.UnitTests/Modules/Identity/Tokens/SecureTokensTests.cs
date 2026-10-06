using Pickory.Modules.Identity.Tokens;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Tokens;

// Kiểm tra token ngẫu nhiên và hash của token. Thuật toán giữ nguyên bản Node để phiên cũ trong DB vẫn dùng được.
public class SecureTokensTests
{
    [Fact]
    // Token là 32 byte ngẫu nhiên dạng hex chữ thường, giống định dạng của bản Node.
    public void GenerateToken_Returns32BytesAsLowercaseHex()
    {
        var firstToken = SecureTokens.GenerateToken();
        var secondToken = SecureTokens.GenerateToken();

        // 64 ký tự hex tương ứng 32 byte. Hai lần gọi cho hai token khác nhau.
        Assert.Matches("^[0-9a-f]{64}$", firstToken);
        Assert.NotEqual(firstToken, secondToken);
    }

    [Fact]
    // Hash của token là SHA-256 dạng hex, cùng kết quả với bản Node.
    public void HashToken_MatchesNodeSha256HexDigest()
    {
        // Giá trị chuẩn của SHA-256 cho "abc". Bản Node trả cùng chuỗi hex, nên phiên cũ trong DB vẫn khớp.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SecureTokens.HashToken("abc"));
    }

    [Fact]
    // Hash ổn định giữa các lần gọi và khác token gốc. DB tra phiên bằng hash và không lưu token gốc.
    public void HashToken_IsStableAndDiffersFromToken()
    {
        var token = SecureTokens.GenerateToken();

        var firstHash = SecureTokens.HashToken(token);

        // Băm lại cùng token cho cùng hash, nên middleware tìm được phiên.
        Assert.Equal(firstHash, SecureTokens.HashToken(token));
        // Hash khác token gốc và có định dạng hex 64 ký tự.
        Assert.NotEqual(token, firstHash);
        Assert.Matches("^[0-9a-f]{64}$", firstHash);
    }

    [Theory]
    [InlineData("abcdef123456", "abcdef123456", true)]
    [InlineData("abcdef123456", "abcdef123457", false)]
    [InlineData("abcdef123456", "short", false)]
    // Phép so sánh thời gian cố định vẫn phải đúng: khác ký tự cuối hoặc khác độ dài đều cho false.
    public void SafeEqual_ComparesWholeValue(string left, string right, bool expected)
    {
        Assert.Equal(expected, SecureTokens.SafeEqual(left, right));
    }
}
