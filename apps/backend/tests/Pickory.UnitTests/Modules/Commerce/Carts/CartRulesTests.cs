using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Carts;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Carts;

public sealed class CartRulesTests
{
    private static SkuSnapshot Sku(
        string skuId = "sku-1",
        string productStatus = "active",
        bool enabled = true,
        int price = 100_000,
        int? compareAtPrice = null,
        int stock = 10) =>
        new(skuId, $"CODE-{skuId}", "product-1", "Sản phẩm thử", "san-pham-thu", productStatus, enabled,
            new Dictionary<string, string> { ["Màu"] = "Đỏ" }, price, compareAtPrice, stock, 300, 20, 10, 2, null);

    [Fact]
    // SKU không còn trong kết quả catalog thì dòng giỏ có nhãn `removed`.
    public void FindUnavailableReason_SkuMissingFromCatalog_ReturnsRemoved()
    {
        Assert.Equal(CartUnavailableReason.Removed, CartRules.FindUnavailableReason(null, quantity: 1));
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("hidden")]
    // Sản phẩm nháp hoặc ẩn không được lộ tên và giá cho khách, nên coi như đã bị gỡ.
    public void FindUnavailableReason_ProductNotVisibleToCustomer_ReturnsRemoved(string productStatus)
    {
        var reason = CartRules.FindUnavailableReason(Sku(productStatus: productStatus), quantity: 1);

        Assert.Equal(CartUnavailableReason.Removed, reason);
    }

    [Fact]
    public void FindUnavailableReason_DiscontinuedProduct_ReturnsProductUnavailable()
    {
        var reason = CartRules.FindUnavailableReason(Sku(productStatus: "discontinued"), quantity: 1);

        Assert.Equal(CartUnavailableReason.ProductUnavailable, reason);
    }

    [Fact]
    public void FindUnavailableReason_DisabledSku_ReturnsSkuDisabled()
    {
        var reason = CartRules.FindUnavailableReason(Sku(enabled: false), quantity: 1);

        Assert.Equal(CartUnavailableReason.SkuDisabled, reason);
    }

    [Fact]
    public void FindUnavailableReason_ZeroStock_ReturnsOutOfStock()
    {
        var reason = CartRules.FindUnavailableReason(Sku(stock: 0), quantity: 1);

        Assert.Equal(CartUnavailableReason.OutOfStock, reason);
    }

    [Fact]
    public void FindUnavailableReason_QuantityAboveStock_ReturnsInsufficientStock()
    {
        var reason = CartRules.FindUnavailableReason(Sku(stock: 2), quantity: 3);

        Assert.Equal(CartUnavailableReason.InsufficientStock, reason);
    }

    [Fact]
    // Số lượng đúng bằng tồn kho vẫn mua được. Đây là giá trị biên giữa `>` và `>=`.
    public void FindUnavailableReason_QuantityEqualsStock_ReturnsNull()
    {
        Assert.Null(CartRules.FindUnavailableReason(Sku(stock: 3), quantity: 3));
    }

    [Fact]
    // Khi nhiều lý do cùng đúng, nhãn nói nguyên nhân gốc: sản phẩm ngừng bán thắng SKU tắt, SKU tắt thắng hết hàng.
    public void FindUnavailableReason_SeveralReasons_ReportsRootCause()
    {
        var discontinuedAndDisabled = Sku(productStatus: "discontinued", enabled: false, stock: 0);
        var disabledAndEmpty = Sku(enabled: false, stock: 0);

        Assert.Equal(CartUnavailableReason.ProductUnavailable, CartRules.FindUnavailableReason(discontinuedAndDisabled, 1));
        Assert.Equal(CartUnavailableReason.SkuDisabled, CartRules.FindUnavailableReason(disabledAndEmpty, 1));
    }

    [Theory]
    [InlineData("active", true, 5, AddCheck.Ok)]
    [InlineData("draft", true, 5, AddCheck.NotFound)]
    [InlineData("hidden", true, 5, AddCheck.NotFound)]
    [InlineData("discontinued", true, 5, AddCheck.Unavailable)]
    [InlineData("active", false, 5, AddCheck.Unavailable)]
    [InlineData("active", true, 0, AddCheck.Unavailable)]
    public void CheckAddable_ClassifiesSku(string productStatus, bool enabled, int stock, AddCheck expected)
    {
        var sku = Sku(productStatus: productStatus, enabled: enabled, stock: stock);

        Assert.Equal(expected, CartRules.CheckAddable(sku));
    }

    [Fact]
    public void CheckAddable_MissingSku_ReturnsNotFound()
    {
        Assert.Equal(AddCheck.NotFound, CartRules.CheckAddable(null));
    }

    [Fact]
    // Hàng vượt tồn kho vẫn thêm được, vì giỏ không giữ hàng. Dòng chỉ mang nhãn `insufficient_stock`.
    public void CheckAddable_QuantityIsNotComparedWithStock()
    {
        Assert.Equal(AddCheck.Ok, CartRules.CheckAddable(Sku(stock: 1)));
    }

    [Theory]
    [InlineData(5, 5, false)]
    [InlineData(6, 5, true)]
    [InlineData(1, 5, false)]
    public void ExceedsQuantityLimit_UsesInclusiveMaximum(int quantity, int maximum, bool expected)
    {
        Assert.Equal(expected, CartRules.ExceedsQuantityLimit(quantity, maximum));
    }

    [Theory]
    [InlineData(20, 20, false)]
    [InlineData(21, 20, true)]
    public void ExceedsLineLimit_UsesInclusiveMaximum(int lineCount, int maximum, bool expected)
    {
        Assert.Equal(expected, CartRules.ExceedsLineLimit(lineCount, maximum));
    }
}
