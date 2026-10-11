using System.Net;
using System.Text.Json;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-04: thêm, đổi số lượng và xóa dòng giỏ. Mỗi test dùng mã ẩn danh riêng nên không đụng giỏ của test khác.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CartWriteTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly CartTestKit _kit;

    public CartWriteTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _kit = new CartTestKit(factory, database);
    }

    public Task InitializeAsync() => _kit.InitializeAsync();

    public Task DisposeAsync() => _kit.CleanupAsync();

    private static int QuantityOf(JsonElement cart, string skuId) =>
        CartTestKit.LineOf(cart, skuId).GetProperty("quantity").GetInt32();

    [Fact]
    // Quyết định 3: POST đầu tiên tạo giỏ và dòng. Response trả giỏ cùng dạng với GET.
    public async Task Post_FirstItem_CreatesCartAndReturnsCart()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync(price: 100_000)).SkuIds[0];

        var response = await _kit.AddAsync(anonymousId, skuId, quantity: 2);

        var cart = await CartTestKit.ReadCartAsync(response);
        Assert.Equal(2, QuantityOf(cart, skuId));
        Assert.Equal(200_000, cart.GetProperty("subtotal").GetInt32());
        var cartRow = Assert.Single(await _kit.CartsOfAnonymousAsync(anonymousId));
        var itemRow = Assert.Single(cartRow.Items);
        Assert.Equal(skuId, itemRow.SkuId);
        Assert.Equal(2, itemRow.Quantity);
        Assert.Null(cartRow.UserId);
    }

    [Fact]
    // Quyết định 6: thêm cùng SKU thì cộng số lượng, không tạo dòng thứ hai.
    public async Task Post_SameSku_AddsQuantityToExistingLine()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId, quantity: 2);

        var cart = await CartTestKit.ReadCartAsync(await _kit.AddAsync(anonymousId, skuId, quantity: 2));

        Assert.Equal(4, QuantityOf(cart, skuId));
        Assert.Single(cart.GetProperty("items").EnumerateArray());
    }

    [Fact]
    // Quyết định 6: tổng sau khi cộng vượt giới hạn thì trả 422 và giữ nguyên dòng cũ.
    public async Task Post_SameSkuOverQuantityLimit_Returns422AndKeepsOldQuantity()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId, quantity: 4);

        var response = await _kit.AddAsync(anonymousId, skuId, quantity: 2);

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "CART_LIMIT_EXCEEDED");
        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId));
        Assert.Equal(4, QuantityOf(cart, skuId));
    }

    [Fact]
    // Tiêu chí 3: dòng mới có số lượng vượt giới hạn mỗi SKU cũng trả 422. Đúng bằng giới hạn thì được.
    public async Task Post_NewLineQuantity_IsLimitedToFivePerSku()
    {
        var anonymousId = _kit.NewAnonymousId();
        var atLimitSku = (await _kit.AddProductAsync()).SkuIds[0];
        var overLimitSku = (await _kit.AddProductAsync()).SkuIds[0];

        var atLimit = await _kit.AddAsync(anonymousId, atLimitSku, quantity: 5);
        var overLimit = await _kit.AddAsync(anonymousId, overLimitSku, quantity: 6);

        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
        await CartTestKit.AssertErrorAsync(overLimit, HttpStatusCode.UnprocessableEntity, "CART_LIMIT_EXCEEDED");
        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId));
        Assert.Single(cart.GetProperty("items").EnumerateArray());
    }

    [Fact]
    // Quyết định 7: dòng mới làm số dòng vượt giới hạn thì trả 422. Thêm vào dòng đã có vẫn được.
    public async Task Post_NewLineOverLineLimit_Returns422_ButExistingLineStillAccepts()
    {
        await _kit.SetLimitsAsync(maxQuantityPerSku: 5, maxCartLines: 2);
        var anonymousId = _kit.NewAnonymousId();
        var skuIds = await _kit.AddSkusAsync(3);
        await _kit.AddAsync(anonymousId, skuIds[0]);
        await _kit.AddAsync(anonymousId, skuIds[1]);

        var overLine = await _kit.AddAsync(anonymousId, skuIds[2]);
        var existing = await _kit.AddAsync(anonymousId, skuIds[0]);

        await CartTestKit.AssertErrorAsync(overLine, HttpStatusCode.UnprocessableEntity, "CART_LIMIT_EXCEEDED");
        Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId));
        Assert.Equal(2, cart.GetProperty("items").GetArrayLength());
    }

    [Fact]
    // Quyết định 8: giới hạn đọc từ `commerce_settings`, và response báo đúng giá trị đang áp dụng.
    public async Task Limits_AreReadFromSettingsRow()
    {
        await _kit.SetLimitsAsync(maxQuantityPerSku: 2, maxCartLines: 7);
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];

        var overLimit = await _kit.AddAsync(anonymousId, skuId, quantity: 3);
        var atLimit = await _kit.AddAsync(anonymousId, skuId, quantity: 2);

        await CartTestKit.AssertErrorAsync(overLimit, HttpStatusCode.UnprocessableEntity, "CART_LIMIT_EXCEEDED");
        var cart = await CartTestKit.ReadCartAsync(atLimit);
        Assert.Equal(2, cart.GetProperty("maxQuantityPerSku").GetInt32());
        Assert.Equal(7, cart.GetProperty("maxCartLines").GetInt32());
    }

    [Fact]
    // Quyết định 6: PATCH đặt số lượng mới. Vượt giới hạn thì trả 422 và giữ số cũ.
    public async Task Patch_SetsQuantity_AndRejectsAboveLimit()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId, quantity: 4);

        var lowered = await _kit.SetQuantityAsync(anonymousId, skuId, 1);
        var overLimit = await _kit.SetQuantityAsync(anonymousId, skuId, 6);

        Assert.Equal(1, QuantityOf(await CartTestKit.ReadCartAsync(lowered), skuId));
        await CartTestKit.AssertErrorAsync(overLimit, HttpStatusCode.UnprocessableEntity, "CART_LIMIT_EXCEEDED");
        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId));
        Assert.Equal(1, QuantityOf(cart, skuId));
    }

    [Fact]
    public async Task Delete_RemovesLine_AndReturnsCart()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuIds = await _kit.AddSkusAsync(2);
        await _kit.AddAsync(anonymousId, skuIds[0]);
        await _kit.AddAsync(anonymousId, skuIds[1]);

        var cart = await CartTestKit.ReadCartAsync(await _kit.RemoveAsync(anonymousId, skuIds[0]));

        var remaining = Assert.Single(cart.GetProperty("items").EnumerateArray());
        Assert.Equal(skuIds[1], remaining.GetProperty("skuId").GetString());
    }

    [Fact]
    // Tiêu chí 4: SKU không tồn tại trả 404 và không tạo giỏ.
    public async Task Post_UnknownSku_Returns404AndCreatesNoCart()
    {
        var anonymousId = _kit.NewAnonymousId();

        var response = await _kit.AddAsync(anonymousId, "sku-khong-ton-tai");

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "SKU_NOT_FOUND");
        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
    }

    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Hidden)]
    // Quyết định 9: SKU của sản phẩm nháp hoặc ẩn trả 404, giống SKU lạ. API không lộ sản phẩm chưa công khai.
    public async Task Post_SkuOfProductNotPublished_Returns404(ProductStatus status)
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync(status: status)).SkuIds[0];

        var response = await _kit.AddAsync(anonymousId, skuId);

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.NotFound, "SKU_NOT_FOUND");
        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("out_of_stock")]
    [InlineData("discontinued")]
    // Quyết định 10: SKU tắt, hết hàng hoặc sản phẩm ngừng bán không thêm được, trả 422 `SKU_UNAVAILABLE`.
    public async Task Post_UnavailableSku_Returns422(string scenario)
    {
        var anonymousId = _kit.NewAnonymousId();
        var product = scenario switch
        {
            "disabled" => await _kit.AddProductAsync(enabled: false),
            "out_of_stock" => await _kit.AddProductAsync(stock: 0),
            _ => await _kit.AddProductAsync(status: ProductStatus.Discontinued),
        };

        var response = await _kit.AddAsync(anonymousId, product.SkuIds[0]);

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "SKU_UNAVAILABLE");
        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
    }

    [Fact]
    // Giỏ không giữ hàng, nên số lượng lớn hơn tồn kho vẫn thêm được. Dòng mang nhãn `insufficient_stock`.
    public async Task Post_QuantityAboveStock_IsAcceptedWithInsufficientStockLabel()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync(stock: 1)).SkuIds[0];

        var cart = await CartTestKit.ReadCartAsync(await _kit.AddAsync(anonymousId, skuId, quantity: 3));

        var line = CartTestKit.LineOf(cart, skuId);
        Assert.Equal("insufficient_stock", line.GetProperty("unavailableReason").GetString());
    }

    [Theory]
    [InlineData("""{"skuId":"{sku}","quantity":0}""")]
    [InlineData("""{"skuId":"{sku}","quantity":-1}""")]
    [InlineData("""{"skuId":"{sku}","quantity":1.5}""")]
    [InlineData("""{"skuId":"{sku}","quantity":"2"}""")]
    [InlineData("""{"skuId":"{sku}","quantity":null}""")]
    [InlineData("""{"skuId":"{sku}"}""")]
    // Tiêu chí 4 và quyết định 12: số lượng không phải số nguyên từ 1 trở lên trả 400, và `details` chỉ chỉ ô `quantity`.
    public async Task Post_InvalidQuantity_Returns400OnQuantityField(string bodyTemplate)
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];

        var response = await _kit.SendAsync(
            HttpMethod.Post, "/api/cart/items", anonymousId, bodyTemplate.Replace("{sku}", skuId));

        var error = await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var field = Assert.Single(error.GetProperty("details").EnumerateArray());
        Assert.Equal("quantity", field.GetProperty("key").GetString());
        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
    }

    [Theory]
    [InlineData("""{"quantity":1}""")]
    [InlineData("""{"skuId":"","quantity":1}""")]
    [InlineData("""{"skuId":7,"quantity":1}""")]
    public async Task Post_MissingOrInvalidSkuId_Returns400OnSkuIdField(string body)
    {
        var anonymousId = _kit.NewAnonymousId();

        var response = await _kit.SendAsync(HttpMethod.Post, "/api/cart/items", anonymousId, body);

        var error = await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var field = Assert.Single(error.GetProperty("details").EnumerateArray());
        Assert.Equal("skuId", field.GetProperty("key").GetString());
    }

    [Fact]
    public async Task Post_BodyIsNotAnObject_Returns400()
    {
        var response = await _kit.SendAsync(HttpMethod.Post, "/api/cart/items", _kit.NewAnonymousId(), "[1,2]");

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    // Tiêu chí 5: `source` dài hơn 64 ký tự trả 400. Đúng 64 ký tự thì được.
    public async Task Post_SourceLongerThan64_Returns400_AndExactly64IsAccepted()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];

        var tooLong = await _kit.AddAsync(anonymousId, skuId, source: new string('a', 65));
        var atLimit = await _kit.AddAsync(anonymousId, skuId, source: new string('b', 64));

        var error = await CartTestKit.AssertErrorAsync(tooLong, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("source", Assert.Single(error.GetProperty("details").EnumerateArray()).GetProperty("key").GetString());
        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
        Assert.Single(_kit.AddedEventsOf(anonymousId));
    }

    [Fact]
    public async Task Post_SourceIsNotString_Returns400()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];

        var response = await _kit.SendAsync(
            HttpMethod.Post, "/api/cart/items", anonymousId, $$"""{"skuId":"{{skuId}}","quantity":1,"source":5}""");

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    // Tiêu chí 5 và quyết định 14: `source` đi vào sự kiện. Chuỗi rỗng hoặc toàn khoảng trắng coi như không có.
    public async Task Post_Source_FlowsIntoEvent_AndBlankSourceIsNull()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuIds = await _kit.AddSkusAsync(3);

        await _kit.AddAsync(anonymousId, skuIds[0], quantity: 2, source: "shopping-assistant");
        await _kit.AddAsync(anonymousId, skuIds[1], source: "   ");
        await _kit.AddAsync(anonymousId, skuIds[2]);

        var events = _kit.AddedEventsOf(anonymousId);
        Assert.Equal(3, events.Count);
        var assistant = events.Single(item => item.SkuId == skuIds[0]);
        Assert.Equal("shopping-assistant", assistant.Source);
        Assert.Equal(2, assistant.Quantity);
        Assert.Null(events.Single(item => item.SkuId == skuIds[1]).Source);
        Assert.Null(events.Single(item => item.SkuId == skuIds[2]).Source);
    }

    [Fact]
    // Quyết định 14: POST thành công phát đúng 1 sự kiện, mang số lượng vừa thêm chứ không phải tổng trong giỏ.
    public async Task Post_Success_EmitsExactlyOneEventWithAddedQuantity()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];

        await _kit.AddAsync(anonymousId, skuId, quantity: 2);
        await _kit.AddAsync(anonymousId, skuId, quantity: 1);

        var events = _kit.AddedEventsOf(anonymousId);
        Assert.Equal([2, 1], events.Select(item => item.Quantity));
        Assert.All(events, item =>
        {
            Assert.Equal(skuId, item.SkuId);
            Assert.Null(item.UserId);
        });
    }

    [Fact]
    public async Task Post_LoggedInCustomer_EventCarriesUserId()
    {
        var (userId, sessionToken) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];

        await _kit.AddAsync(anonymousId, skuId, sessionToken: sessionToken);

        var added = Assert.Single(_kit.AddedEventsOf(anonymousId));
        Assert.Equal(userId, added.UserId);
    }

    [Fact]
    // Quyết định 14: DELETE thành công phát đúng 1 sự kiện xóa. PATCH không phát sự kiện nào.
    public async Task Delete_Success_EmitsOneRemovedEvent_AndPatchEmitsNone()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId, quantity: 2);

        await _kit.SetQuantityAsync(anonymousId, skuId, 3);
        Assert.Empty(_kit.RemovedEventsOf(anonymousId));
        Assert.Single(_kit.AddedEventsOf(anonymousId));

        await _kit.RemoveAsync(anonymousId, skuId);

        var removed = Assert.Single(_kit.RemovedEventsOf(anonymousId));
        Assert.Equal(skuId, removed.SkuId);
        Assert.Single(_kit.AddedEventsOf(anonymousId));
    }

    [Fact]
    // Quyết định 14: mọi lỗi (validation, giới hạn, SKU lạ, không khả dụng) không phát sự kiện.
    public async Task FailedWrites_EmitNoEvents()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        var disabledSku = (await _kit.AddProductAsync(enabled: false)).SkuIds[0];

        await _kit.AddAsync(anonymousId, skuId, quantity: 0);
        await _kit.AddAsync(anonymousId, skuId, quantity: 6);
        await _kit.AddAsync(anonymousId, "sku-khong-ton-tai");
        await _kit.AddAsync(anonymousId, disabledSku);
        await _kit.RemoveAsync(anonymousId, skuId);
        await _kit.SetQuantityAsync(anonymousId, skuId, 2);

        Assert.Empty(_kit.AddedEventsOf(anonymousId));
        Assert.Empty(_kit.RemovedEventsOf(anonymousId));
    }

    [Fact]
    // Quyết định 11: PATCH và DELETE SKU không có trong giỏ trả 404 `CART_ITEM_NOT_FOUND`.
    public async Task PatchAndDelete_SkuNotInCart_Return404()
    {
        var anonymousId = _kit.NewAnonymousId();
        var inCart = (await _kit.AddProductAsync()).SkuIds[0];
        var notInCart = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.AddAsync(anonymousId, inCart);

        var patch = await _kit.SetQuantityAsync(anonymousId, notInCart, 2);
        var delete = await _kit.RemoveAsync(anonymousId, notInCart);

        await CartTestKit.AssertErrorAsync(patch, HttpStatusCode.NotFound, "CART_ITEM_NOT_FOUND");
        await CartTestKit.AssertErrorAsync(delete, HttpStatusCode.NotFound, "CART_ITEM_NOT_FOUND");
    }

    [Fact]
    // Khách chưa có giỏ cũng nhận 404 khi PATCH hoặc DELETE, và API không tạo giỏ trống.
    public async Task PatchAndDelete_VisitorWithoutCart_Return404AndCreateNoCart()
    {
        var anonymousId = _kit.NewAnonymousId();

        var patch = await _kit.SetQuantityAsync(anonymousId, "sku-bat-ky", 2);
        var delete = await _kit.RemoveAsync(anonymousId, "sku-bat-ky");

        await CartTestKit.AssertErrorAsync(patch, HttpStatusCode.NotFound, "CART_ITEM_NOT_FOUND");
        await CartTestKit.AssertErrorAsync(delete, HttpStatusCode.NotFound, "CART_ITEM_NOT_FOUND");
        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
    }

    [Theory]
    [InlineData("""{"quantity":0}""")]
    [InlineData("""{"quantity":-3}""")]
    [InlineData("""{"quantity":1.5}""")]
    [InlineData("""{}""")]
    // Quyết định 12 áp cho cả PATCH. Số lượng sai trả 400 trước khi tra giỏ, nên lỗi không phụ thuộc SKU có trong giỏ.
    public async Task Patch_InvalidQuantity_Returns400OnQuantityField(string body)
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId, quantity: 2);

        var response = await _kit.SendAsync(new HttpMethod("PATCH"), $"/api/cart/items/{skuId}", anonymousId, body);

        var error = await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("quantity", Assert.Single(error.GetProperty("details").EnumerateArray()).GetProperty("key").GetString());
        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(anonymousId));
        Assert.Equal(2, QuantityOf(cart, skuId));
    }

    [Fact]
    // Quyết định 1: route ghi công khai vẫn đi qua kiểm `Origin`. Thiếu `Origin` trả 403 và không đổi giỏ.
    public async Task WriteRoutes_WithoutOrigin_Return403()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];

        var post = await _kit.SendAsync(
            HttpMethod.Post, "/api/cart/items", anonymousId, new { skuId, quantity = 1 }, withOrigin: false);
        var patch = await _kit.SendAsync(
            new HttpMethod("PATCH"), $"/api/cart/items/{skuId}", anonymousId, new { quantity = 1 }, withOrigin: false);
        var delete = await _kit.SendAsync(
            HttpMethod.Delete, $"/api/cart/items/{skuId}", anonymousId, withOrigin: false);

        foreach (var response in new[] { post, patch, delete })
        {
            await CartTestKit.AssertErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN_ORIGIN");
        }

        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
    }

    [Fact]
    // Tiêu chí 7 và quyết định 15: thêm, đổi số lượng và xóa không đổi tồn kho, và không ghi movement.
    public async Task CartWrites_DoNotChangeStockOrWriteMovements()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync(stock: 7)).SkuIds[0];
        var movementsBefore = await _kit.MovementCountOfAsync(skuId);

        await _kit.AddAsync(anonymousId, skuId, quantity: 3);
        var stockAfterAdd = await _kit.StockOfAsync(skuId);
        await _kit.SetQuantityAsync(anonymousId, skuId, 5);
        await _kit.RemoveAsync(anonymousId, skuId);

        Assert.Equal(7, stockAfterAdd);
        Assert.Equal(7, await _kit.StockOfAsync(skuId));
        Assert.Equal(movementsBefore, await _kit.MovementCountOfAsync(skuId));
    }
}
