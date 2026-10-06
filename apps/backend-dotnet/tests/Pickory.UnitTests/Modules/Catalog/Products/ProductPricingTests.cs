using Pickory.Modules.Catalog.Products;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Products;

// Port từ `domain/pricing.test.ts` của bản Node (CAT-02).
public sealed class ProductPricingTests
{
    [Fact]
    // Giá hiển thị là giá thấp nhất trong các SKU đang bật và còn hàng.
    public void ComputeDisplayPrice_UsesCheapestEnabledInStockSku()
    {
        var result = ProductPricing.ComputeDisplayPrice(
        [
            new SkuPricingInput(20_000_000, 5, Enabled: true),
            new SkuPricingInput(18_000_000, 3, Enabled: true),
            new SkuPricingInput(15_000_000, 0, Enabled: true),
            new SkuPricingInput(12_000_000, 10, Enabled: false),
        ]);

        Assert.Equal(new DisplayPrice(18_000_000, InStock: true), result);
    }

    [Fact]
    // Hết hàng toàn bộ thì vẫn hiển thị giá thấp nhất của SKU đang bật để khách tham khảo.
    public void ComputeDisplayPrice_FallsBackToEnabledSkusWhenAllOutOfStock()
    {
        var result = ProductPricing.ComputeDisplayPrice(
        [
            new SkuPricingInput(25_000_000, 0, Enabled: true),
            new SkuPricingInput(22_000_000, 0, Enabled: true),
            new SkuPricingInput(19_000_000, 10, Enabled: false),
        ]);

        Assert.Equal(new DisplayPrice(22_000_000, InStock: false), result);
    }

    [Fact]
    // Không có SKU đang bật thì không có giá và không còn hàng.
    public void ComputeDisplayPrice_NoEnabledSku()
    {
        var result = ProductPricing.ComputeDisplayPrice(
        [
            new SkuPricingInput(30_000_000, 10, Enabled: false),
            new SkuPricingInput(28_000_000, 5, Enabled: false),
        ]);

        Assert.Equal(new DisplayPrice(null, InStock: false), result);
    }

    [Fact]
    // Danh sách SKU rỗng cũng không có giá.
    public void ComputeDisplayPrice_EmptyList()
    {
        Assert.Equal(new DisplayPrice(null, InStock: false), ProductPricing.ComputeDisplayPrice([]));
    }

    [Theory]
    [InlineData(15_000_000, null, true)]
    [InlineData(15_000_000, 18_000_000d, true)]
    [InlineData(0, null, false)]
    [InlineData(-500_000, null, false)]
    [InlineData(15_000_000.5, null, false)]
    [InlineData(15_000_000, 15_000_000d, false)]
    [InlineData(15_000_000, 12_000_000d, false)]
    [InlineData(15_000_000, 18_000_000.5, false)]
    // Giá bán là số nguyên dương. Giá niêm yết để trống hoặc là số nguyên lớn hơn giá bán.
    public void ValidatePrice_FollowsBusinessRules(double price, double? compareAtPrice, bool expectedValid)
    {
        var result = ProductPricing.ValidatePrice((decimal)price, compareAtPrice is null ? null : (decimal)compareAtPrice);

        Assert.Equal(expectedValid, result.Valid);
        Assert.Equal(expectedValid, result.Error is null);
    }
}
