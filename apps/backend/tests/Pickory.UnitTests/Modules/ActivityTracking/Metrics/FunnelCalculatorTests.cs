using Pickory.Modules.ActivityTracking.Contracts.Metrics;
using Pickory.Modules.ActivityTracking.Metrics;
using Xunit;

namespace Pickory.UnitTests.Modules.ActivityTracking.Metrics;

// ACT-02: hàm thuần tính phễu từ tổng ba chỉ số, gồm thứ tự bước, số lượt và tỉ lệ so với bước trước.
public sealed class FunnelCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsThreeStepsInFunnelOrder()
    {
        var funnel = FunnelCalculator.Calculate(productViews: 100, addToCarts: 20, ordersPlaced: 5);

        Assert.Equal(
            new[] { "product_view", "add_to_cart", "order_placed" },
            funnel.Steps.Select(step => step.Metric));
    }

    [Fact]
    public void Calculate_KeepsCountOfEachStepInItsOwnPosition()
    {
        var funnel = FunnelCalculator.Calculate(productViews: 100, addToCarts: 20, ordersPlaced: 5);

        Assert.Equal(new long[] { 100, 20, 5 }, funnel.Steps.Select(step => step.Count));
    }

    [Fact]
    public void Calculate_FirstStepHasNoConversionRate()
    {
        var funnel = FunnelCalculator.Calculate(productViews: 100, addToCarts: 20, ordersPlaced: 5);

        Assert.Null(funnel.Steps[0].ConversionRate);
    }

    [Fact]
    public void Calculate_RateIsCountOverPreviousStepCount()
    {
        var funnel = FunnelCalculator.Calculate(productViews: 100, addToCarts: 20, ordersPlaced: 5);

        Assert.Equal(new decimal?[] { null, 0.2m, 0.25m }, funnel.Steps.Select(step => step.ConversionRate));
    }

    [Fact]
    public void Calculate_AllZero_ReturnsZeroCountsAndNullRatesWithoutThrowing()
    {
        var funnel = FunnelCalculator.Calculate(productViews: 0, addToCarts: 0, ordersPlaced: 0);

        Assert.Equal(new long[] { 0, 0, 0 }, funnel.Steps.Select(step => step.Count));
        Assert.All(funnel.Steps, step => Assert.Null(step.ConversionRate));
    }

    [Fact]
    public void Calculate_NoProductViews_SecondStepRateIsNullAndThirdIsFraction()
    {
        var funnel = FunnelCalculator.Calculate(productViews: 0, addToCarts: 3, ordersPlaced: 1);

        Assert.Null(funnel.Steps[1].ConversionRate);
        Assert.Equal(1m / 3m, funnel.Steps[2].ConversionRate);
    }

    [Fact]
    public void Calculate_NoAddToCarts_SecondStepRateIsZeroAndThirdIsNull()
    {
        var funnel = FunnelCalculator.Calculate(productViews: 5, addToCarts: 0, ordersPlaced: 0);

        Assert.Equal(0m, funnel.Steps[1].ConversionRate);
        Assert.Null(funnel.Steps[2].ConversionRate);
    }

    // 3 đơn từ 2 lượt thêm giỏ cho tỉ lệ 1,5. Hàm không cắt về 1, vì số đếm của hai bước độc lập nhau.
    [Fact]
    public void Calculate_MoreOrdersThanAddToCarts_KeepsRateAboveOne()
    {
        var funnel = FunnelCalculator.Calculate(productViews: 10, addToCarts: 2, ordersPlaced: 3);

        Assert.Equal(1.5m, funnel.Steps[2].ConversionRate);
    }

    [Fact]
    public void Calculate_CountsNearLongMaxValue_DoNotOverflow()
    {
        var funnel = FunnelCalculator.Calculate(
            productViews: long.MaxValue, addToCarts: long.MaxValue, ordersPlaced: long.MaxValue);

        Assert.Equal(new long[] { long.MaxValue, long.MaxValue, long.MaxValue }, funnel.Steps.Select(step => step.Count));
        Assert.Equal(new decimal?[] { null, 1m, 1m }, funnel.Steps.Select(step => step.ConversionRate));
    }

    // Bộ đếm chỉ tăng, nên số âm là dữ liệu hỏng. Phễu không được hiện tỉ lệ âm.
    [Theory]
    [InlineData(-1, 0, 0, "productViews")]
    [InlineData(0, -1, 0, "addToCarts")]
    [InlineData(0, 0, -1, "ordersPlaced")]
    [InlineData(long.MinValue, 0, 0, "productViews")]
    public void Calculate_NegativeCount_ThrowsForThatParameter(
        long productViews, long addToCarts, long ordersPlaced, string expectedParamName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            FunnelCalculator.Calculate(productViews: productViews, addToCarts: addToCarts, ordersPlaced: ordersPlaced));

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Fact]
    public void MetricNames_MatchStoredCounterKeys()
    {
        Assert.Equal("product_view", ActivityMetricNames.ProductView);
        Assert.Equal("add_to_cart", ActivityMetricNames.AddToCart);
        Assert.Equal("order_placed", ActivityMetricNames.OrderPlaced);
    }
}
