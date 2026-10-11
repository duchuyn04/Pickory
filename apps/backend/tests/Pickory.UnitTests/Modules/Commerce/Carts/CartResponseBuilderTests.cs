using Pickory.Modules.Catalog.Contracts.Reading;
using Pickory.Modules.Commerce.Carts;
using Pickory.Modules.Commerce.Settings;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Carts;

public sealed class CartResponseBuilderTests
{
    private static readonly CommerceLimits Limits = new(500_000, 20_000_000, MaxQuantityPerSku: 5, MaxCartLines: 20);

    private static SkuSnapshot Sku(
        string skuId, int price = 100_000, int? compareAtPrice = null, int stock = 10,
        bool enabled = true, string productStatus = "active") =>
        new(skuId, $"CODE-{skuId}", $"product-{skuId}", $"Sản phẩm {skuId}", $"san-pham-{skuId}", productStatus, enabled,
            new Dictionary<string, string> { ["Màu"] = "Đỏ" }, price, compareAtPrice, stock, 300, 20, 10, 2,
            new ProductImageInfo($"https://img.test/{skuId}.jpg", "Ảnh", null));

    private static CartResponse Build(IReadOnlyList<CartLineInput> lines, params SkuSnapshot[] skus) =>
        CartResponseBuilder.Build(lines, skus.ToDictionary(sku => sku.SkuId), Limits);

    [Fact]
    public void Build_EmptyCart_ReturnsZeroSubtotalAndLimits()
    {
        var response = Build([]);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.Subtotal);
        Assert.False(response.HasUnavailableItems);
        Assert.Equal(5, response.MaxQuantityPerSku);
        Assert.Equal(20, response.MaxCartLines);
    }

    [Fact]
    // Giá luôn lấy từ catalog tại lúc dựng giỏ. Dòng giỏ không mang giá riêng.
    public void Build_PurchasableLine_UsesCatalogPriceAndComputesLineTotal()
    {
        var response = Build([new("a", 3)], Sku("a", price: 250_000, compareAtPrice: 300_000));

        var line = Assert.Single(response.Items);
        Assert.Equal(250_000, line.UnitPrice);
        Assert.Equal(300_000, line.CompareAtPrice);
        Assert.Equal(750_000, line.LineTotal);
        Assert.True(line.Purchasable);
        Assert.Null(line.UnavailableReason);
        Assert.Equal(750_000, response.Subtotal);
        Assert.False(response.HasUnavailableItems);
    }

    [Fact]
    // Dòng không mua được vẫn hiện giá để khách thấy mình đã chọn gì, nhưng không cộng vào tạm tính.
    public void Build_UnavailableLine_KeepsPriceButIsExcludedFromSubtotal()
    {
        var response = Build(
            [new("a", 1), new("b", 2)],
            Sku("a", price: 100_000),
            Sku("b", price: 400_000, enabled: false));

        var disabledLine = response.Items.Single(line => line.SkuId == "b");
        Assert.False(disabledLine.Purchasable);
        Assert.Equal(CartUnavailableReason.SkuDisabled, disabledLine.UnavailableReason);
        Assert.Equal(800_000, disabledLine.LineTotal);
        Assert.Equal(100_000, response.Subtotal);
        Assert.True(response.HasUnavailableItems);
    }

    [Fact]
    // Dòng `removed` không có dữ liệu catalog, nên mọi trường sản phẩm và giá là null.
    public void Build_RemovedLine_HasNullProductAndPriceFields()
    {
        var response = Build([new("ghost", 2)]);

        var line = Assert.Single(response.Items);
        Assert.Equal("ghost", line.SkuId);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(CartUnavailableReason.Removed, line.UnavailableReason);
        Assert.False(line.Purchasable);
        Assert.Null(line.ProductId);
        Assert.Null(line.ProductName);
        Assert.Null(line.ProductSlug);
        Assert.Null(line.OptionValues);
        Assert.Null(line.Image);
        Assert.Null(line.UnitPrice);
        Assert.Null(line.CompareAtPrice);
        Assert.Null(line.LineTotal);
        Assert.Null(line.Stock);
        Assert.Equal(0, response.Subtotal);
        Assert.True(response.HasUnavailableItems);
    }

    [Fact]
    // Sản phẩm nháp không được lộ tên hay giá qua giỏ, kể cả khi SKU từng nằm trong giỏ.
    public void Build_DraftProduct_DoesNotLeakProductFields()
    {
        var response = Build([new("a", 1)], Sku("a", productStatus: "draft"));

        var line = Assert.Single(response.Items);
        Assert.Equal(CartUnavailableReason.Removed, line.UnavailableReason);
        Assert.Null(line.ProductName);
        Assert.Null(line.UnitPrice);
    }

    [Fact]
    // Thứ tự theo `skuId` giữ ổn định giữa các lần xem, dù dòng DB trả về thứ tự nào.
    public void Build_OrdersLinesBySkuId()
    {
        var response = Build([new("c", 1), new("a", 1), new("b", 1)], Sku("a"), Sku("b"), Sku("c"));

        Assert.Equal(["a", "b", "c"], response.Items.Select(line => line.SkuId));
    }

    [Fact]
    public void Build_QuantityAboveStock_LabelsInsufficientStockAndShowsStock()
    {
        var response = Build([new("a", 4)], Sku("a", stock: 2));

        var line = Assert.Single(response.Items);
        Assert.Equal(CartUnavailableReason.InsufficientStock, line.UnavailableReason);
        Assert.Equal(2, line.Stock);
        Assert.False(line.Purchasable);
    }
}
