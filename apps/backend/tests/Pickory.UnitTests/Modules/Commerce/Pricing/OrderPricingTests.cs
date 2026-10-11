using Pickory.Modules.Commerce.Pricing;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Pricing;

public sealed class OrderPricingTests
{
    private const int Threshold = 500_000;

    [Fact]
    // Tạm tính bằng tổng của đơn giá nhân số lượng trên mọi dòng.
    public void Calculate_SumsUnitPriceTimesQuantity()
    {
        PricedLine[] lines = [new(100_000, 2), new(35_000, 3)];

        var result = OrderPricing.Calculate(lines, carrierShippingFee: 30_000, Threshold);

        Assert.Equal(305_000, result.Subtotal);
    }

    [Fact]
    // Tạm tính dưới ngưỡng thì khách trả đủ phí GHN và shop không chịu phần nào.
    public void Calculate_BelowThreshold_CustomerPaysFullShippingFee()
    {
        PricedLine[] lines = [new(499_999, 1)];

        var result = OrderPricing.Calculate(lines, carrierShippingFee: 32_000, Threshold);

        Assert.Equal(32_000, result.ShippingFee);
        Assert.Equal(0, result.ShippingDiscount);
        Assert.Equal(32_000, result.CustomerShippingFee);
        Assert.Equal(531_999, result.Total);
    }

    [Fact]
    // Tạm tính đúng bằng ngưỡng thì được miễn phí. Giá trị biên này là chỗ dễ sai giữa `>` và `>=`.
    public void Calculate_ExactlyAtThreshold_ShopBearsShippingFee()
    {
        PricedLine[] lines = [new(250_000, 2)];

        var result = OrderPricing.Calculate(lines, carrierShippingFee: 32_000, Threshold);

        Assert.Equal(500_000, result.Subtotal);
        Assert.Equal(32_000, result.ShippingFee);
        Assert.Equal(32_000, result.ShippingDiscount);
        Assert.Equal(0, result.CustomerShippingFee);
        Assert.Equal(500_000, result.Total);
    }

    [Fact]
    // Tạm tính trên ngưỡng thì phần shop chịu bằng phí GHN và khách trả 0.
    public void Calculate_AboveThreshold_ShopBearsShippingFee()
    {
        PricedLine[] lines = [new(900_000, 1)];

        var result = OrderPricing.Calculate(lines, carrierShippingFee: 45_000, Threshold);

        Assert.Equal(45_000, result.ShippingDiscount);
        Assert.Equal(0, result.CustomerShippingFee);
        Assert.Equal(900_000, result.Total);
    }

    [Fact]
    // Tổng tiền luôn bằng tạm tính cộng phí GHN trừ phần shop chịu.
    public void Calculate_TotalEqualsSubtotalPlusFeeMinusDiscount()
    {
        PricedLine[][] scenarios =
        [
            [new(10_000, 1)],
            [new(250_000, 2)],
            [new(1_200_000, 3), new(80_000, 5)],
        ];

        foreach (var lines in scenarios)
        {
            var result = OrderPricing.Calculate(lines, carrierShippingFee: 28_000, Threshold);

            Assert.Equal(result.Subtotal + result.ShippingFee - result.ShippingDiscount, result.Total);
        }
    }

    [Fact]
    // Giỏ rỗng có tạm tính 0. Với ngưỡng dương, đơn này không được miễn phí.
    public void Calculate_WithNoLines_ReturnsZeroSubtotalAndFullFee()
    {
        var result = OrderPricing.Calculate([], carrierShippingFee: 20_000, Threshold);

        Assert.Equal(0, result.Subtotal);
        Assert.Equal(20_000, result.CustomerShippingFee);
        Assert.Equal(20_000, result.Total);
    }

    [Fact]
    // Phí GHN bằng 0 thì không có phần nào để shop chịu.
    public void Calculate_WithZeroShippingFee_HasNoDiscount()
    {
        PricedLine[] lines = [new(600_000, 1)];

        var result = OrderPricing.Calculate(lines, carrierShippingFee: 0, Threshold);

        Assert.Equal(0, result.ShippingDiscount);
        Assert.Equal(600_000, result.Total);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1000, 0)]
    [InlineData(1000, -2)]
    // Đơn giá âm hoặc số lượng nhỏ hơn 1 làm tổng tiền sai, nên hàm từ chối.
    public void Calculate_WithInvalidLine_Throws(int unitPrice, int quantity)
    {
        PricedLine[] lines = [new(unitPrice, quantity)];

        Assert.Throws<ArgumentOutOfRangeException>(() => OrderPricing.Calculate(lines, 20_000, Threshold));
    }

    [Fact]
    // Phí GHN âm sẽ làm tổng tiền thấp hơn tạm tính.
    public void Calculate_WithNegativeShippingFee_Throws()
    {
        PricedLine[] lines = [new(100_000, 1)];

        Assert.Throws<ArgumentOutOfRangeException>(() => OrderPricing.Calculate(lines, -1, Threshold));
    }

    [Fact]
    // Ngưỡng âm làm mọi đơn được miễn phí. Cấu hình sai không được lọt vào phép tính.
    public void Calculate_WithNegativeThreshold_Throws()
    {
        PricedLine[] lines = [new(100_000, 1)];

        Assert.Throws<ArgumentOutOfRangeException>(() => OrderPricing.Calculate(lines, 20_000, -1));
    }

    [Fact]
    // Tổng vượt `int` phải báo lỗi. Quay vòng thành số âm sẽ cho khách mua gần như miễn phí.
    public void Calculate_WhenSubtotalOverflowsInt_Throws()
    {
        PricedLine[] lines = [new(int.MaxValue, 2)];

        Assert.Throws<OverflowException>(() => OrderPricing.Calculate(lines, 20_000, Threshold));
    }

    [Theory]
    [InlineData(19_999_999, true)]
    [InlineData(20_000_000, true)]
    [InlineData(20_000_001, false)]
    // Tổng bằng hạn mức vẫn chọn được COD. Tổng trên hạn mức bị từ chối.
    public void IsCodAllowed_ComparesTotalWithLimit(int total, bool expected)
    {
        Assert.Equal(expected, OrderPricing.IsCodAllowed(total, 20_000_000));
    }
}
