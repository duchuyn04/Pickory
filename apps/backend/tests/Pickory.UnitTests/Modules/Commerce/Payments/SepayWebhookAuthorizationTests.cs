using Pickory.Modules.Commerce.Payments;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Payments;

public sealed class SepayWebhookAuthorizationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer test-key")]
    [InlineData("apikey test-key")]
    [InlineData("Apikey  test-key")]
    [InlineData(" Apikey test-key")]
    [InlineData("Apikey test-key ")]
    [InlineData("Apikey ")]
    [InlineData("Apikey wrong-key")]
    [InlineData("Apikey test-key,Apikey test-key")]
    public void Matches_InvalidHeader_Rejects(string? header)
    {
        // Dạng header phải chính xác để không chấp nhận nhiều cách diễn giải khóa.
        Assert.False(SepayWebhookAuthorization.Matches("test-key", header));
    }

    [Fact]
    public void Matches_ConfiguredKey_AcceptsExactHeader()
    {
        Assert.True(SepayWebhookAuthorization.Matches("test-key", "Apikey test-key"));
    }

    [Fact]
    public void Matches_MissingConfiguration_Rejects()
    {
        // Máy chưa cấu hình không được mở quyền nhận giao dịch.
        Assert.False(SepayWebhookAuthorization.Matches("", "Apikey test-key"));
    }
}
