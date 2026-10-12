using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Shipping.Contracts.Parcels;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Checkout;

public sealed class CheckoutParcelRulesTests
{
    private static CheckoutLine Line(
        string skuId = "sku-1", int quantity = 1, int? weight = 300, int? length = 20, int? width = 10, int? height = 2) =>
        new(skuId, quantity, UnitPrice: 100_000, weight, length, width, height);

    [Fact]
    public void FindSkuIdsMissingDimensions_AllLinesComplete_ReturnsEmpty()
    {
        var result = CheckoutParcelRules.FindSkuIdsMissingDimensions([Line("a"), Line("b")]);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("weight")]
    [InlineData("length")]
    [InlineData("width")]
    [InlineData("height")]
    public void FindSkuIdsMissingDimensions_OneMeasureIsNull_ReturnsThatSku(string missing)
    {
        var line = Line(
            "thieu",
            weight: missing == "weight" ? null : 300,
            length: missing == "length" ? null : 20,
            width: missing == "width" ? null : 10,
            height: missing == "height" ? null : 2);

        var result = CheckoutParcelRules.FindSkuIdsMissingDimensions([Line("du"), line]);

        Assert.Equal(["thieu"], result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void FindSkuIdsMissingDimensions_NonPositiveMeasure_CountsAsMissing(int value)
    {
        var result = CheckoutParcelRules.FindSkuIdsMissingDimensions([Line("khong", weight: value)]);

        Assert.Equal(["khong"], result);
    }

    [Fact]
    public void FindSkuIdsMissingDimensions_SeveralLinesMissing_KeepsCartOrder()
    {
        var result = CheckoutParcelRules.FindSkuIdsMissingDimensions(
            [Line("b", height: null), Line("ok"), Line("a", weight: null)]);

        Assert.Equal(["b", "a"], result);
    }

    [Fact]
    public void BuildParcelItems_MapsEachLineToItsOwnUnitParcelAndQuantity()
    {
        var items = CheckoutParcelRules.BuildParcelItems(
        [
            Line("a", quantity: 2, weight: 300, length: 20, width: 10, height: 2),
            Line("b", quantity: 1, weight: 1200, length: 35, width: 25, height: 8),
        ]);

        Assert.Equal(
            [
                new ParcelItem(new Parcel(300, 20, 10, 2), 2),
                new ParcelItem(new Parcel(1200, 35, 25, 8), 1),
            ],
            items);
    }

    [Fact]
    public void BuildParcelItems_LineMissingDimensions_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => CheckoutParcelRules.BuildParcelItems([Line("thieu", weight: null)]));
    }
}
