using Pickory.Modules.Identity.Tokens;
using Xunit;

namespace Pickory.UnitTests.Modules.Identity.Tokens;

public class SecureTokensTests
{
    [Fact]
    public void GenerateToken_Returns32BytesAsLowercaseHex()
    {
        var firstToken = SecureTokens.GenerateToken();
        var secondToken = SecureTokens.GenerateToken();

        Assert.Matches("^[0-9a-f]{64}$", firstToken);
        Assert.NotEqual(firstToken, secondToken);
    }

    [Fact]
    public void HashToken_MatchesNodeSha256HexDigest()
    {
        // Giá trị chuẩn của SHA-256 cho "abc". Bản Node trả cùng chuỗi hex, nên phiên cũ trong DB vẫn khớp.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SecureTokens.HashToken("abc"));
    }

    [Fact]
    public void HashToken_IsStableAndDiffersFromToken()
    {
        var token = SecureTokens.GenerateToken();

        var firstHash = SecureTokens.HashToken(token);

        Assert.Equal(firstHash, SecureTokens.HashToken(token));
        Assert.NotEqual(token, firstHash);
        Assert.Matches("^[0-9a-f]{64}$", firstHash);
    }

    [Theory]
    [InlineData("abcdef123456", "abcdef123456", true)]
    [InlineData("abcdef123456", "abcdef123457", false)]
    [InlineData("abcdef123456", "short", false)]
    public void SafeEqual_ComparesWholeValue(string left, string right, bool expected)
    {
        Assert.Equal(expected, SecureTokens.SafeEqual(left, right));
    }
}
