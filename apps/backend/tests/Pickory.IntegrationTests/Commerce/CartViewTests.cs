using System.Net;
using System.Text.Json;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-04: xem giỏ. Giá và trạng thái luôn lấy mới từ catalog, dòng không mua được vẫn hiện kèm nhãn lý do.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CartViewTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly CartTestKit _kit;

    public CartViewTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _kit = new CartTestKit(factory, database);
    }

    public Task InitializeAsync() => _kit.InitializeAsync();

    public Task DisposeAsync() => _kit.CleanupAsync();

    public enum CatalogChange
    {
        SkuDisabled,
        ProductDiscontinued,
        OutOfStock,
        QuantityAboveStock,
        ProductDraft,
        ProductHidden,
        ProductDeleted,
    }

    [Fact]
    // Quyết định 3: xem giỏ chưa tồn tại trả giỏ rỗng và không tạo dòng DB.
    public async Task Get_VisitorWithoutCart_ReturnsEmptyCartAndCreatesNoRow()
    {
        var anonymousId = _kit.NewAnonymousId();

        var response = await _kit.GetCartAsync(anonymousId);

        var cart = await CartTestKit.ReadCartAsync(response);
        Assert.Empty(cart.GetProperty("items").EnumerateArray());
        Assert.Equal(0, cart.GetProperty("subtotal").GetInt32());
        Assert.False(cart.GetProperty("hasUnavailableItems").GetBoolean());
        // Chưa có dòng `commerce_settings` nên giới hạn là mặc định trong mã nguồn (quyết định 8).
        Assert.Equal(5, cart.GetProperty("maxQuantityPerSku").GetInt32());
        Assert.Equal(20, cart.GetProperty("maxCartLines").GetInt32());
        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
    }

    [Fact]
    // Tiêu chí 1: giá lấy mới từ catalog ở mỗi lần xem, không lưu trong giỏ.
    public async Task Get_ReadsCurrentCatalogPriceOnEveryView()
    {
        var anonymousId = _kit.NewAnonymousId();
        var product = await _kit.AddProductAsync(price: 100_000, compareAtPrice: 150_000);
        var skuId = product.SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId, quantity: 2);

        var before = CartTestKit.LineOf(await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId)), skuId);
        await _kit.UpdateSkuAsync(skuId, sku =>
        {
            sku.Price = 120_000;
            sku.CompareAtPrice = null;
        });
        var afterCart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId));
        var after = CartTestKit.LineOf(afterCart, skuId);

        Assert.Equal(100_000, before.GetProperty("unitPrice").GetInt32());
        Assert.Equal(150_000, before.GetProperty("compareAtPrice").GetInt32());
        Assert.Equal(120_000, after.GetProperty("unitPrice").GetInt32());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("compareAtPrice").ValueKind);
        Assert.Equal(240_000, after.GetProperty("lineTotal").GetInt32());
        Assert.Equal(240_000, afterCart.GetProperty("subtotal").GetInt32());
    }

    [Fact]
    // Quyết định 13: dòng có đủ trường sản phẩm, và thứ tự dòng theo `skuId` giữ ổn định.
    public async Task Get_LineHasProductFields_AndLinesAreOrderedBySkuId()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuIds = await _kit.AddSkusAsync(3);
        foreach (var skuId in skuIds)
        {
            await _kit.AddAsync(anonymousId, skuId);
        }

        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId));

        var listed = cart.GetProperty("items").EnumerateArray().Select(line => line.GetProperty("skuId").GetString()).ToList();
        Assert.Equal(skuIds.Order(StringComparer.Ordinal), listed);
        var line = cart.GetProperty("items")[0];
        foreach (var property in new[]
                 {
                     "skuId", "quantity", "productId", "productName", "productSlug", "optionValues", "image",
                     "unitPrice", "compareAtPrice", "lineTotal", "stock", "purchasable", "unavailableReason",
                 })
        {
            Assert.True(line.TryGetProperty(property, out _), $"Thiếu trường {property}");
        }

        Assert.True(line.GetProperty("purchasable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, line.GetProperty("unavailableReason").ValueKind);
    }

    [Theory]
    [InlineData(CatalogChange.SkuDisabled, "sku_disabled")]
    [InlineData(CatalogChange.ProductDiscontinued, "product_unavailable")]
    [InlineData(CatalogChange.OutOfStock, "out_of_stock")]
    [InlineData(CatalogChange.QuantityAboveStock, "insufficient_stock")]
    [InlineData(CatalogChange.ProductDraft, "removed")]
    [InlineData(CatalogChange.ProductHidden, "removed")]
    [InlineData(CatalogChange.ProductDeleted, "removed")]
    // Tiêu chí 2: dòng không mua được vẫn nằm trong giỏ, kèm nhãn lý do và không cộng vào tạm tính.
    public async Task Get_UnavailableLine_StaysInCartWithReason(CatalogChange change, string expectedReason)
    {
        var anonymousId = _kit.NewAnonymousId();
        var product = await _kit.AddProductAsync(price: 200_000, stock: 10);
        var skuId = product.SkuIds[0];
        var healthySku = (await _kit.AddProductAsync(price: 50_000)).SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId, quantity: 3);
        await _kit.AddAsync(anonymousId, healthySku, quantity: 1);

        await ApplyChangeAsync(change, product.Id, skuId);
        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId));

        var line = CartTestKit.LineOf(cart, skuId);
        Assert.Equal(expectedReason, line.GetProperty("unavailableReason").GetString());
        Assert.False(line.GetProperty("purchasable").GetBoolean());
        Assert.Equal(3, line.GetProperty("quantity").GetInt32());
        Assert.True(cart.GetProperty("hasUnavailableItems").GetBoolean());
        // Chỉ dòng còn mua được nằm trong tạm tính.
        Assert.Equal(50_000, cart.GetProperty("subtotal").GetInt32());
        if (expectedReason == "removed")
        {
            // Dòng bị gỡ không lộ tên sản phẩm hay giá, kể cả khi sản phẩm chỉ chuyển sang nháp.
            Assert.Equal(JsonValueKind.Null, line.GetProperty("productName").ValueKind);
            Assert.Equal(JsonValueKind.Null, line.GetProperty("unitPrice").ValueKind);
            Assert.Equal(JsonValueKind.Null, line.GetProperty("lineTotal").ValueKind);
        }
        else
        {
            Assert.Equal(200_000, line.GetProperty("unitPrice").GetInt32());
            Assert.Equal(600_000, line.GetProperty("lineTotal").GetInt32());
        }
    }

    [Fact]
    // Khi hết lý do, dòng mua được trở lại và nhãn biến mất.
    public async Task Get_SkuReEnabled_LineIsPurchasableAgain()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId);
        await _kit.UpdateSkuAsync(skuId, sku => sku.Enabled = false);
        await _kit.UpdateSkuAsync(skuId, sku => sku.Enabled = true);

        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId));

        Assert.True(CartTestKit.LineOf(cart, skuId).GetProperty("purchasable").GetBoolean());
        Assert.False(cart.GetProperty("hasUnavailableItems").GetBoolean());
    }

    [Fact]
    // Quyết định 2: mỗi mã ẩn danh có giỏ riêng. Khách này không thấy hàng của khách kia.
    public async Task Get_DifferentAnonymousVisitors_HaveSeparateCarts()
    {
        var firstVisitor = _kit.NewAnonymousId();
        var secondVisitor = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.AddAsync(firstVisitor, skuId);

        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(secondVisitor));

        Assert.Empty(cart.GetProperty("items").EnumerateArray());
    }

    [Fact]
    // Quyết định 2: khách đăng nhập dùng giỏ theo `UserId`, cột `AnonymousId` để null, và cookie ẩn danh đổi không mất giỏ.
    public async Task Cart_LoggedInCustomer_IsOwnedByUserIdAcrossAnonymousIds()
    {
        var (userId, sessionToken) = await _kit.CreateCustomerAsync();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        var firstBrowser = _kit.NewAnonymousId();
        var secondBrowser = _kit.NewAnonymousId();

        var addResponse = await _kit.AddAsync(firstBrowser, skuId, quantity: 2, sessionToken: sessionToken);
        var otherBrowserCart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(secondBrowser, sessionToken));

        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        Assert.Equal(2, CartTestKit.LineOf(otherBrowserCart, skuId).GetProperty("quantity").GetInt32());
        var carts = await _kit.CartsOfUserAsync(userId);
        var cartRow = Assert.Single(carts);
        Assert.Null(cartRow.AnonymousId);
        Assert.Empty(await _kit.CartsOfAnonymousAsync(firstBrowser));
    }

    [Fact]
    // Khách đã đăng nhập không thấy giỏ vãng lai cũ cho tới khi COM-05 gộp giỏ. Hai giỏ độc lập nhau.
    public async Task Cart_LoggedInCustomer_DoesNotSeeAnonymousCartBeforeMerge()
    {
        var (_, sessionToken) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId);

        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId, sessionToken));

        Assert.Empty(cart.GetProperty("items").EnumerateArray());
    }

    [Fact]
    // Giỏ của tài khoản không được lộ cho khách vãng lai, kể cả khi dòng giỏ có cả `AnonymousId` trùng cookie của khách.
    public async Task Get_GuestWhoseAnonymousIdMatchesAccountCart_DoesNotSeeAccountCart()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.InsertCartAsync(userId, anonymousId, skuId, quantity: 2);

        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId));

        Assert.Empty(cart.GetProperty("items").EnumerateArray());
    }

    private async Task ApplyChangeAsync(CatalogChange change, string productId, string skuId)
    {
        switch (change)
        {
            case CatalogChange.SkuDisabled:
                await _kit.UpdateSkuAsync(skuId, sku => sku.Enabled = false);
                break;
            case CatalogChange.ProductDiscontinued:
                await _kit.SetProductStatusAsync(productId, ProductStatus.Discontinued);
                break;
            case CatalogChange.OutOfStock:
                await _kit.UpdateSkuAsync(skuId, sku => sku.Stock = 0);
                break;
            case CatalogChange.QuantityAboveStock:
                await _kit.UpdateSkuAsync(skuId, sku => sku.Stock = 2);
                break;
            case CatalogChange.ProductDraft:
                await _kit.SetProductStatusAsync(productId, ProductStatus.Draft);
                break;
            case CatalogChange.ProductHidden:
                await _kit.SetProductStatusAsync(productId, ProductStatus.Hidden);
                break;
            case CatalogChange.ProductDeleted:
                await _kit.DeleteProductAsync(productId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }
}
