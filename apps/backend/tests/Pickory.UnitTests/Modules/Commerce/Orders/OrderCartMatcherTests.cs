using Pickory.Modules.Commerce.Carts;
using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class OrderCartMatcherTests
{
    private static CartLineInput Line(string skuId, int quantity) => new(skuId, quantity);

    [Fact]
    public void Matches_SameLinesInDifferentOrder_ReturnsTrue()
    {
        var cart = new[] { Line("a", 1), Line("b", 2) };
        var priced = new[] { Line("b", 2), Line("a", 1) };

        Assert.True(OrderCartMatcher.Matches(cart, priced));
    }

    [Fact]
    public void Matches_QuantityDiffers_ReturnsFalse()
    {
        Assert.False(OrderCartMatcher.Matches([Line("a", 2)], [Line("a", 1)]));
    }

    [Fact]
    public void Matches_CartHasExtraLine_ReturnsFalse()
    {
        Assert.False(OrderCartMatcher.Matches([Line("a", 1), Line("b", 1)], [Line("a", 1)]));
    }

    [Fact]
    public void Matches_CartMissesPricedLine_ReturnsFalse()
    {
        Assert.False(OrderCartMatcher.Matches([Line("a", 1)], [Line("a", 1), Line("b", 1)]));
    }

    [Fact]
    public void Matches_DifferentSkuSameCount_ReturnsFalse()
    {
        Assert.False(OrderCartMatcher.Matches([Line("a", 1)], [Line("b", 1)]));
    }

    [Fact]
    public void Matches_BothEmpty_ReturnsTrue()
    {
        Assert.True(OrderCartMatcher.Matches([], []));
    }
}
