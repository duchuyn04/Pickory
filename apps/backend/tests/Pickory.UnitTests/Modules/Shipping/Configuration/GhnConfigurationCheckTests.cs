using Pickory.Modules.Shipping.Configuration;
using Pickory.Shared.Configuration;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping.Configuration;

// SHIP-02: production thiếu biến GHN thì dừng khi khởi động. Dev và test thiếu biến vẫn chạy.
public sealed class GhnConfigurationCheckTests
{
    private static readonly AppConfig CompleteConfig = new()
    {
        GhnToken = "token-bi-mat",
        GhnShopId = "123456",
        GhnWebhookSecret = "webhook-bi-mat",
    };

    [Fact]
    public void EnsureConfigured_Production_WithAllVariables_DoesNotThrow()
    {
        GhnConfigurationCheck.EnsureConfigured(CompleteConfig, isProduction: true);
    }

    [Theory]
    [InlineData("GHN_TOKEN")]
    [InlineData("GHN_SHOP_ID")]
    [InlineData("GHN_WEBHOOK_SECRET")]
    // Mỗi biến thiếu cho ra lỗi nêu đúng tên biến đó, và không nêu tên biến đã có.
    public void EnsureConfigured_Production_MissingOneVariable_NamesOnlyThatVariable(string missingName)
    {
        var config = missingName switch
        {
            "GHN_TOKEN" => CompleteConfig with { GhnToken = string.Empty },
            "GHN_SHOP_ID" => CompleteConfig with { GhnShopId = string.Empty },
            _ => CompleteConfig with { GhnWebhookSecret = string.Empty },
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => GhnConfigurationCheck.EnsureConfigured(config, isProduction: true));

        Assert.Contains(missingName, exception.Message);
        foreach (var otherName in new[] { "GHN_TOKEN", "GHN_SHOP_ID", "GHN_WEBHOOK_SECRET" }.Where(name => name != missingName))
        {
            Assert.DoesNotContain(otherName, exception.Message);
        }
    }

    [Fact]
    // Thiếu cả ba biến thì thông báo liệt kê đủ ba tên, để người triển khai sửa một lần.
    public void EnsureConfigured_Production_MissingAllVariables_NamesAllOfThem()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => GhnConfigurationCheck.EnsureConfigured(new AppConfig(), isProduction: true));

        Assert.Contains("GHN_TOKEN", exception.Message);
        Assert.Contains("GHN_SHOP_ID", exception.Message);
        Assert.Contains("GHN_WEBHOOK_SECRET", exception.Message);
    }

    [Fact]
    // Thông báo lỗi không được chứa giá trị của biến đã có, để token và secret không lọt vào log.
    public void EnsureConfigured_Production_ErrorDoesNotContainSecretValues()
    {
        var config = CompleteConfig with { GhnShopId = string.Empty };

        var exception = Assert.Throws<InvalidOperationException>(
            () => GhnConfigurationCheck.EnsureConfigured(config, isProduction: true));

        Assert.DoesNotContain("token-bi-mat", exception.Message);
        Assert.DoesNotContain("webhook-bi-mat", exception.Message);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    // Giá trị chỉ có khoảng trắng coi như thiếu. Nếu không, production khởi động mà không gọi được GHN.
    public void EnsureConfigured_Production_BlankValue_CountsAsMissing(string blank)
    {
        var config = CompleteConfig with { GhnToken = blank };

        var exception = Assert.Throws<InvalidOperationException>(
            () => GhnConfigurationCheck.EnsureConfigured(config, isProduction: true));

        Assert.Contains("GHN_TOKEN", exception.Message);
    }

    [Fact]
    // Dev và test thiếu biến vẫn chạy, vì module dùng client giả khi thiếu token.
    public void EnsureConfigured_NotProduction_MissingAllVariables_DoesNotThrow()
    {
        GhnConfigurationCheck.EnsureConfigured(new AppConfig(), isProduction: false);
    }
}
