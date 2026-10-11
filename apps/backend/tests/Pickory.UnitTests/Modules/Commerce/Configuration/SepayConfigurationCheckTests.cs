using Pickory.Modules.Commerce.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Configuration;

public sealed class SepayConfigurationCheckTests
{
    [Fact]
    public void EnsureConfigured_Production_WithKey_DoesNotThrow()
    {
        var config = new AppConfig { SepayWebhookApiKey = "khoa-bi-mat" };

        SepayConfigurationCheck.EnsureConfigured(config, isProduction: true);
    }

    [Fact]
    // Production thiếu khóa thì dừng khi khởi động. Nếu không, webhook SePay không xác thực được và đơn không bao giờ được ghi nhận.
    public void EnsureConfigured_Production_WithoutKey_NamesTheVariable()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => SepayConfigurationCheck.EnsureConfigured(new AppConfig(), isProduction: true));

        Assert.Contains("SEPAY_WEBHOOK_API_KEY", exception.Message);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    // Khóa chỉ có khoảng trắng coi như thiếu.
    public void EnsureConfigured_Production_BlankKey_CountsAsMissing(string blank)
    {
        var config = new AppConfig { SepayWebhookApiKey = blank };

        Assert.Throws<InvalidOperationException>(() => SepayConfigurationCheck.EnsureConfigured(config, isProduction: true));
    }

    [Fact]
    // Dev và test thiếu khóa vẫn chạy.
    public void EnsureConfigured_NotProduction_WithoutKey_DoesNotThrow()
    {
        SepayConfigurationCheck.EnsureConfigured(new AppConfig(), isProduction: false);
    }
}
