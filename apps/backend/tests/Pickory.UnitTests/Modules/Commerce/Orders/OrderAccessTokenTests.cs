using System.Security.Cryptography;
using System.Text;
using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class OrderAccessTokenTests
{
    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    [Fact]
    public void Create_ReturnsThirtyTwoRandomBytesEncodedAsBase64Url()
    {
        var pair = OrderAccessToken.Create();

        Assert.Equal(32, DecodeBase64Url(pair.Token).Length);
        Assert.DoesNotContain('+', pair.Token);
        Assert.DoesNotContain('/', pair.Token);
        Assert.DoesNotContain('=', pair.Token);
    }

    [Fact]
    public void Create_HashIsSha256OfTokenInLowercaseHex()
    {
        var pair = OrderAccessToken.Create();

        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Token))).ToLowerInvariant();
        Assert.Equal(expected, pair.Hash);
        Assert.NotEqual(pair.Token, pair.Hash);
    }

    [Fact]
    public void Create_TwoCallsReturnDifferentTokens()
    {
        Assert.NotEqual(OrderAccessToken.Create().Token, OrderAccessToken.Create().Token);
    }

    [Fact]
    public void Hash_SameTokenGivesSameHash_DifferentTokenGivesDifferentHash()
    {
        Assert.Equal(OrderAccessToken.Hash("abc"), OrderAccessToken.Hash("abc"));
        Assert.NotEqual(OrderAccessToken.Hash("abc"), OrderAccessToken.Hash("abd"));
    }
}
