using Pickory.Modules.Shipping;
using Xunit;

namespace Pickory.UnitTests.Modules.Shipping;

// SHIP-05 lát 2: luật thuần của báo phí, gồm trần bảo hiểm của GHN và gói dịch vụ mặc định.
public sealed class QuoteRulesTests
{
    [Fact]
    public void Limits_MatchGhnDocumentation()
    {
        Assert.Equal(5_000_000, QuoteRules.MaxInsuredValue);
        Assert.Equal(50_000_000, QuoteRules.MaxCodAmount);
        Assert.Equal(2, QuoteRules.LightGoodsServiceTypeId);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4_999_999, 4_999_999)]
    // Đúng bằng trần thì giữ nguyên. Lệch một đồng là sai khai báo bảo hiểm.
    [InlineData(5_000_000, 5_000_000)]
    [InlineData(5_000_001, 5_000_000)]
    [InlineData(12_000_000, 5_000_000)]
    public void ClampInsuredValue_CapsAtGhnLimit(int insuredValue, int expected)
    {
        Assert.Equal(expected, QuoteRules.ClampInsuredValue(insuredValue));
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    public void ResolveServiceTypeId_UsesConfiguredValueOrLightGoods(int? configured, int expected)
    {
        Assert.Equal(expected, QuoteRules.ResolveServiceTypeId(configured));
    }
}
