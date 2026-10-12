using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-08: `POST /api/orders` tính lại tiền, giữ hàng, tạo đơn có snapshot và xóa dòng đã đặt khỏi giỏ trong một transaction.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class PlaceOrderTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string OrdersPath = "/api/orders";
    private const string PreviewPath = "/api/checkout/previews";
    private const string WebOrigin = "http://localhost:5173";

    // Mã và tên lấy theo danh mục của `QuoteTestSetup.SeedCatalogAsync`.
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";
    private const string ProvinceName = "Hồ Chí Minh";
    private const string WardName = "Phường Bến Nghé";

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];
    private readonly List<WebApplicationFactory<Program>> _derivedFactories = [];

    public PlaceOrderTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _kit = new CartTestKit(factory, database);
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        await _kit.InitializeAsync();
        await QuoteTestSetup.ResetAsync(_factory);
        await QuoteTestSetup.SeedCatalogAsync(_factory);
        await QuoteTestSetup.SeedSettingsAsync(_factory, defaultServiceTypeId: 2);

        // Bộ đếm preview là singleton của factory. Xóa trước mỗi test để lượt preview của test trước không chặn test sau.
        _factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
        _factory.GhnClient.ClearFailures();
    }

    public async Task DisposeAsync()
    {
        foreach (var derived in _derivedFactories)
        {
            derived.Dispose();
        }

        _factory.GhnClient.ClearFailures();
        await DeleteOrdersOfThisTestAsync();
        await _kit.CleanupAsync();
        await QuoteTestSetup.ResetAsync(_factory);
    }

    // ----- Dựng dữ liệu và gửi request -----

    private string NewKey()
    {
        var key = "key-" + Guid.NewGuid().ToString("N");
        _keys.Add(key);
        return key;
    }

    private static Dictionary<string, object?> BodyFor(
        int expectedTotal,
        string paymentMethod = "cod",
        object? invoice = null,
        string? note = null,
        string name = "Nguyễn Văn An",
        string phone = "0901234567",
        string email = "an@example.com")
    {
        var body = new Dictionary<string, object?>
        {
            ["contact"] = new { name, phone, email },
            ["address"] = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            ["paymentMethod"] = paymentMethod,
            ["expectedTotal"] = expectedTotal,
        };
        if (note is not null)
        {
            body["note"] = note;
        }

        if (invoice is not null)
        {
            body["invoice"] = invoice;
        }

        return body;
    }

    private static Task<HttpResponseMessage> SendOrderAsync(
        HttpClient client, string anonymousId, string? key, object body, string? sessionToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, OrdersPath);
        var cookie = $"pk_anon={anonymousId}";
        if (sessionToken is not null)
        {
            cookie += $"; pk_session={sessionToken}";
        }

        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", WebOrigin);
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        var json = body as string ?? JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        return client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PlaceAsync(
        string anonymousId, string? key, object body, string? sessionToken = null) =>
        SendOrderAsync(_client, anonymousId, key, body, sessionToken);

    // Thêm một sản phẩm một SKU vào giỏ bằng route giỏ thật và trả id SKU.
    private async Task<string> AddToCartAsync(
        string anonymousId, int price, int quantity = 1, int stock = 10, string? sessionToken = null, string? imagePublicId = null)
    {
        var product = await _kit.AddProductAsync(price: price, stock: stock, imagePublicId: imagePublicId);
        var skuId = product.SkuIds[0];
        var response = await _kit.AddAsync(anonymousId, skuId, quantity, sessionToken: sessionToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return skuId;
    }

    // Tổng tiền mà khách thấy ở preview. Trang checkout gửi lại đúng số này làm `expectedTotal`.
    private async Task<int> PreviewTotalAsync(string anonymousId, string paymentMethod = "cod", string? sessionToken = null)
    {
        var body = new
        {
            address = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            paymentMethod,
        };
        var response = await _kit.SendAsync(HttpMethod.Post, PreviewPath, anonymousId, body, sessionToken);
        var data = await CartTestKit.ReadCartAsync(response);
        return data.GetProperty("total").GetInt32();
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var json = await CartTestKit.ReadJsonAsync(response);
        Assert.True(response.StatusCode == expected, $"Mong đợi {(int)expected} nhưng nhận {(int)response.StatusCode}: {json}");
        return json.GetProperty("data");
    }

    private static string[] DetailKeys(JsonElement error) =>
        error.GetProperty("details").EnumerateArray().Select(item => item.GetProperty("key").GetString()!).ToArray();

    private async Task<Order> LoadOrderAsync(string code)
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking()
            .Include(order => order.Items)
            .Include(order => order.StatusHistory)
            .SingleAsync(order => order.Code == code);
    }

    private async Task<int> OrderCountAsync(string key)
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        return await commerce.Orders.CountAsync(order => order.IdempotencyKey == key);
    }

    private async Task<int> OrderCountForKeysAsync()
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        return await commerce.Orders.CountAsync(order => _keys.Contains(order.IdempotencyKey));
    }

    private async Task<int> CartLineCountAsync(string anonymousId)
    {
        var carts = await _kit.CartsOfAnonymousAsync(anonymousId);
        return carts.Sum(cart => cart.Items.Count);
    }

    private async Task DeleteOrdersOfThisTestAsync()
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        var orderIds = await commerce.Orders
            .Where(order => _keys.Contains(order.IdempotencyKey))
            .Select(order => order.Id)
            .ToListAsync();
        await commerce.OrderStatusHistory.Where(history => orderIds.Contains(history.OrderId)).ExecuteDeleteAsync();
        await commerce.OrderItems.Where(item => orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
        await commerce.Orders.Where(order => orderIds.Contains(order.Id)).ExecuteDeleteAsync();
    }

    // Đặt dòng cấu hình commerce có ngưỡng và hạn mức do test chọn. `CleanupAsync` của kit xóa dòng này.
    private async Task SetSettingsAsync(int freeShippingThreshold, int codMaxAmount)
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        commerce.Settings.Add(new CommerceSettings
        {
            Id = "default",
            FreeShippingThreshold = freeShippingThreshold,
            CodMaxAmount = codMaxAmount,
            MaxQuantityPerSku = 5,
            MaxCartLines = 20,
            UpdatedAt = DateTime.UtcNow,
        });
        await commerce.SaveChangesAsync();
    }

    // Phí của `FakeGhnClient`: 20.000 đ cộng 2.000 đ cho mỗi 500 g làm tròn lên, cộng giá trị khai giá chia 200.
    private static int FakeFee(int weightGrams, int insuredValue) =>
        20_000 + (2_000 * ((weightGrams + 499) / 500)) + (insuredValue / 200);

    // Host test dựng thêm, với dịch vụ do test thay. Host dùng chung DB với factory gốc nhưng có danh sách sự kiện riêng.
    private sealed record DerivedHost(HttpClient Client, RecordedDomainEvents Events);

    private DerivedHost CreateHostWith(Action<IServiceCollection> configure)
    {
        var derived = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(configure));
        _derivedFactories.Add(derived);
        var client = derived.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return new DerivedHost(client, derived.Services.GetRequiredService<RecordedDomainEvents>());
    }

    // Bọc client GHN để chạy một hàm của test ngay sau khi báo phí xong, sau lúc server đã tính tiền.
    // Cách này chèn thay đổi vào đúng khe giữa tính tiền và transaction, nên kết quả không phụ thuộc lịch luồng.
    private sealed class CallbackAfterFeeGhnClient(IGhnClient inner, Func<Task> afterFee) : IGhnClient
    {
        private int _called;

        public async Task<GhnFee> CalculateFeeAsync(GhnFeeRequest request, CancellationToken cancellationToken)
        {
            var fee = await inner.CalculateFeeAsync(request, cancellationToken);
            // Chỉ chạy một lần, để lần tính phí khác của cùng host không đổi dữ liệu thêm.
            if (Interlocked.Exchange(ref _called, 1) == 0)
            {
                await afterFee();
            }

            return fee;
        }

        public Task<IReadOnlyList<GhnProvince>> GetProvincesAsync(CancellationToken cancellationToken) =>
            inner.GetProvincesAsync(cancellationToken);

        public Task<IReadOnlyList<GhnWard>> GetWardsAsync(string provinceId, int offset, int limit, CancellationToken cancellationToken) =>
            inner.GetWardsAsync(provinceId, offset, limit, cancellationToken);

        public Task<GhnLeadTime> GetLeadTimeAsync(GhnLeadTimeRequest request, CancellationToken cancellationToken) =>
            inner.GetLeadTimeAsync(request, cancellationToken);

        public Task<GhnCreatedOrder> CreateOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken) =>
            inner.CreateOrderAsync(request, cancellationToken);

        public Task CancelOrderAsync(string orderCode, string reasonCode, string? reason, CancellationToken cancellationToken) =>
            inner.CancelOrderAsync(orderCode, reasonCode, reason, cancellationToken);

        public Task<GhnOrderDetail> GetOrderDetailAsync(string orderCode, CancellationToken cancellationToken) =>
            inner.GetOrderDetailAsync(orderCode, cancellationToken);
    }

    private DerivedHost CreateHostWithCallbackAfterFee(Func<Task> afterFee) =>
        CreateHostWith(services =>
        {
            services.RemoveAll<IGhnClient>();
            services.AddSingleton<IGhnClient>(provider =>
                new CallbackAfterFeeGhnClient(provider.GetRequiredService<FakeGhnClient>(), afterFee));
        });

    // ----- Tiêu chí 1: Idempotency-Key -----

    [Fact]
    public async Task PlaceOrder_WithoutIdempotencyKey_Returns400Required_AndChangesNothing()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);

        var response = await PlaceAsync(anonymousId, key: null, BodyFor(total));

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "IDEMPOTENCY_KEY_REQUIRED");
        Assert.Equal(10, await _kit.StockOfAsync(skuId));
        Assert.Equal(1, await CartLineCountAsync(anonymousId));
    }

    [Fact]
    public async Task PlaceOrder_WithMalformedIdempotencyKey_Returns400ValidationError()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);

        var response = await PlaceAsync(anonymousId, "short", BodyFor(total));

        var error = await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(["idempotencyKey"], DetailKeys(error));
    }

    [Fact]
    public async Task PlaceOrder_SameKeyTwice_Returns201ThenSameOrderWith200_AndKeepsOneOrderAndOneEvent()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 2);
        var total = await PreviewTotalAsync(anonymousId);
        var key = NewKey();

        var first = await PlaceAsync(anonymousId, key, BodyFor(total));
        var second = await PlaceAsync(anonymousId, key, BodyFor(total));

        var firstData = await ReadDataAsync(first, HttpStatusCode.Created);
        var secondData = await ReadDataAsync(second, HttpStatusCode.OK);
        var code = firstData.GetProperty("code").GetString()!;
        Assert.Equal(code, secondData.GetProperty("code").GetString());
        // Lần gửi lại không còn token gốc, vì server chỉ lưu hash.
        Assert.False(string.IsNullOrEmpty(firstData.GetProperty("accessToken").GetString()));
        Assert.Equal(JsonValueKind.Null, secondData.GetProperty("accessToken").ValueKind);
        Assert.Equal(firstData.GetProperty("total").GetInt32(), secondData.GetProperty("total").GetInt32());
        Assert.Equal(1, await OrderCountAsync(key));
        // Lần gửi lại không giữ hàng thêm: tồn kho chỉ giảm đúng số lượng của một đơn.
        Assert.Equal(8, await _kit.StockOfAsync(skuId));
        Assert.Single(_factory.DomainEvents.OfType<OrderPlacedEvent>(), placed => placed.Code == code);
    }

    [Fact]
    public async Task PlaceOrder_SameKeyFromOtherOwner_Returns409Conflict_WithoutLeakingOrderCode()
    {
        var ownerAnonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(ownerAnonymousId, price: 100_000);
        var ownerTotal = await PreviewTotalAsync(ownerAnonymousId);
        var key = NewKey();
        var created = await ReadDataAsync(await PlaceAsync(ownerAnonymousId, key, BodyFor(ownerTotal)), HttpStatusCode.Created);
        var code = created.GetProperty("code").GetString()!;
        var otherAnonymousId = _kit.NewAnonymousId();
        var otherSkuId = await AddToCartAsync(otherAnonymousId, price: 100_000);
        var otherTotal = await PreviewTotalAsync(otherAnonymousId);

        var response = await PlaceAsync(otherAnonymousId, key, BodyFor(otherTotal));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("IDEMPOTENCY_KEY_CONFLICT", text);
        Assert.DoesNotContain(code, text);
        Assert.Equal(1, await OrderCountAsync(key));
        Assert.Equal(10, await _kit.StockOfAsync(otherSkuId));
    }

    [Fact]
    public async Task PlaceOrder_ReplayByLoggedInUserFromAnotherBrowser_Returns200WithSameOrder()
    {
        var (userId, sessionToken) = await _kit.CreateCustomerAsync();
        var firstAnonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(firstAnonymousId, price: 100_000, sessionToken: sessionToken);
        var total = await PreviewTotalAsync(firstAnonymousId, sessionToken: sessionToken);
        var key = NewKey();
        var created = await ReadDataAsync(
            await PlaceAsync(firstAnonymousId, key, BodyFor(total), sessionToken), HttpStatusCode.Created);

        var otherBrowserAnonymousId = _kit.NewAnonymousId();
        var replay = await PlaceAsync(otherBrowserAnonymousId, key, BodyFor(total), sessionToken);

        var replayData = await ReadDataAsync(replay, HttpStatusCode.OK);
        Assert.Equal(created.GetProperty("code").GetString(), replayData.GetProperty("code").GetString());
        var order = await LoadOrderAsync(created.GetProperty("code").GetString()!);
        Assert.Equal(userId, order.UserId);
        Assert.Equal(firstAnonymousId, order.AnonymousId);
    }

    [Fact]
    public async Task PlaceOrder_GuestReusesKeyOfAccountOrderOnSameBrowser_Returns409WithoutLeakingCode()
    {
        var (_, sessionToken) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000, sessionToken: sessionToken);
        var total = await PreviewTotalAsync(anonymousId, sessionToken: sessionToken);
        var key = NewKey();
        var created = await ReadDataAsync(
            await PlaceAsync(anonymousId, key, BodyFor(total), sessionToken), HttpStatusCode.Created);
        var code = created.GetProperty("code").GetString()!;

        // Cùng cookie `pk_anon` nhưng không đăng nhập. Đơn của tài khoản không được trả cho khách vãng lai.
        var response = await PlaceAsync(anonymousId, key, BodyFor(total));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("IDEMPOTENCY_KEY_CONFLICT", text);
        Assert.DoesNotContain(code, text);
        Assert.Equal(1, await OrderCountAsync(key));
    }

    [Fact]
    public async Task PlaceOrder_TwoConcurrentRequestsWithSameKey_CreateOneOrderAndNeverReturn500()
    {
        // Lặp nhiều vòng để các lịch luồng khác nhau cùng được thử. Kết quả đúng với mọi lịch luồng.
        for (var round = 0; round < 8; round++)
        {
            _factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
            var anonymousId = _kit.NewAnonymousId();
            var skuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 2);
            var total = await PreviewTotalAsync(anonymousId);
            var key = NewKey();

            var responses = await Task.WhenAll(
                PlaceAsync(anonymousId, key, BodyFor(total)),
                PlaceAsync(anonymousId, key, BodyFor(total)));

            var statuses = responses.Select(response => (int)response.StatusCode).OrderBy(status => status).ToArray();
            Assert.Equal([200, 201], statuses);
            var codes = new List<string>();
            foreach (var response in responses)
            {
                var json = await CartTestKit.ReadJsonAsync(response);
                codes.Add(json.GetProperty("data").GetProperty("code").GetString()!);
            }

            Assert.Equal(codes[0], codes[1]);
            Assert.Equal(1, await OrderCountAsync(key));
            Assert.Equal(8, await _kit.StockOfAsync(skuId));
            Assert.Single(_factory.DomainEvents.OfType<OrderPlacedEvent>(), placed => placed.Code == codes[0]);
        }
    }

    [Fact]
    public async Task PlaceOrder_TwoConcurrentRequestsWithSameKeyFromDifferentOwners_OneWinsAndOtherGets409()
    {
        for (var round = 0; round < 6; round++)
        {
            _factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
            var firstAnonymousId = _kit.NewAnonymousId();
            var secondAnonymousId = _kit.NewAnonymousId();
            var firstSkuId = await AddToCartAsync(firstAnonymousId, price: 100_000);
            var secondSkuId = await AddToCartAsync(secondAnonymousId, price: 100_000);
            var firstTotal = await PreviewTotalAsync(firstAnonymousId);
            var secondTotal = await PreviewTotalAsync(secondAnonymousId);
            var key = NewKey();

            var responses = await Task.WhenAll(
                PlaceAsync(firstAnonymousId, key, BodyFor(firstTotal)),
                PlaceAsync(secondAnonymousId, key, BodyFor(secondTotal)));

            var statuses = responses.Select(response => (int)response.StatusCode).OrderBy(status => status).ToArray();
            Assert.Equal([201, 409], statuses);
            Assert.Equal(1, await OrderCountAsync(key));
            // Bên thua đã giữ hàng trong transaction của mình, nhưng transaction bị hủy nên tồn kho của nó không đổi.
            var stocks = new[] { await _kit.StockOfAsync(firstSkuId), await _kit.StockOfAsync(secondSkuId) };
            Assert.Equal([9, 10], stocks.OrderBy(stock => stock).ToArray());
        }
    }

    // ----- Tiêu chí 2: bỏ qua số tiền của client, so tổng với preview -----

    [Fact]
    public async Task PlaceOrder_IgnoresMoneyFieldsFromClient_AndUsesServerPrices()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000, quantity: 2);
        var total = await PreviewTotalAsync(anonymousId);
        var body = BodyFor(total);
        body["subtotal"] = 1;
        body["shippingFee"] = 0;
        body["shippingDiscount"] = 999_999;
        body["total"] = 1;
        body["items"] = new[] { new { skuId = "x", unitPrice = 1, quantity = 1 } };
        var key = NewKey();

        var response = await PlaceAsync(anonymousId, key, body);

        var data = await ReadDataAsync(response, HttpStatusCode.Created);
        var fee = FakeFee(weightGrams: 600, insuredValue: 200_000);
        Assert.Equal(200_000, data.GetProperty("subtotal").GetInt32());
        Assert.Equal(fee, data.GetProperty("shippingFee").GetInt32());
        Assert.Equal(0, data.GetProperty("shippingDiscount").GetInt32());
        Assert.Equal(200_000 + fee, data.GetProperty("total").GetInt32());
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        Assert.Equal(200_000, order.Subtotal);
        Assert.Equal(200_000 + fee, order.Total);
        Assert.Equal(100_000, order.Items.Single().UnitPrice);
    }

    [Fact]
    public async Task PlaceOrder_ExpectedTotalDiffersFromServerTotal_Returns409AndChangesNothing()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);
        var key = NewKey();

        var response = await PlaceAsync(anonymousId, key, BodyFor(total - 1));

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.Conflict, "ORDER_TOTAL_CHANGED");
        Assert.Equal(0, await OrderCountAsync(key));
        Assert.Equal(10, await _kit.StockOfAsync(skuId));
        Assert.Equal(1, await CartLineCountAsync(anonymousId));
        Assert.DoesNotContain(_factory.DomainEvents.OfType<OrderPlacedEvent>(), placed => placed.AnonymousId == anonymousId);
    }

    [Fact]
    public async Task PlaceOrder_CartLinesChangeAfterPricingButTotalStaysEqual_Returns409AndChangesNothing()
    {
        // Giỏ gồm 2 x 300.000 đ. Sau khi server tính tiền, giỏ đổi thành 1 x 600.000 đ có cùng tổng, nên chỉ so dòng mới bắt được.
        var anonymousId = _kit.NewAnonymousId();
        var firstSkuId = await AddToCartAsync(anonymousId, price: 300_000, quantity: 2);
        var replacement = await _kit.AddProductAsync(price: 600_000, stock: 10);
        var replacementSkuId = replacement.SkuIds[0];
        var total = await PreviewTotalAsync(anonymousId);
        var key = NewKey();
        var host = CreateHostWithCallbackAfterFee(async () =>
        {
            await using var commerce = _kit.Database.CreateCommerceContext();
            var cart = await commerce.Carts.Include(value => value.Items)
                .SingleAsync(value => value.AnonymousId == anonymousId);
            commerce.CartItems.RemoveRange(cart.Items);
            commerce.CartItems.Add(new CartItem { CartId = cart.Id, SkuId = replacementSkuId, Quantity = 1 });
            await commerce.SaveChangesAsync();
        });

        var response = await SendOrderAsync(host.Client, anonymousId, key, BodyFor(total));

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.Conflict, "ORDER_TOTAL_CHANGED");
        Assert.Equal(0, await OrderCountAsync(key));
        Assert.Empty(host.Events.OfType<OrderPlacedEvent>());
        Assert.Equal(10, await _kit.StockOfAsync(firstSkuId));
        Assert.Equal(10, await _kit.StockOfAsync(replacementSkuId));
        var cartAfter = Assert.Single(await _kit.CartsOfAnonymousAsync(anonymousId));
        var line = Assert.Single(cartAfter.Items);
        Assert.Equal(replacementSkuId, line.SkuId);
    }

    // ----- Tiêu chí 3: lỗi của preview đi ra đúng mã -----

    [Fact]
    public async Task PlaceOrder_SkuNotPurchasable_Returns422SkuUnavailable()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000);
        await _kit.UpdateSkuAsync(skuId, sku => sku.Enabled = false);

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(expectedTotal: 120_000));

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "SKU_UNAVAILABLE");
        Assert.Equal(10, await _kit.StockOfAsync(skuId));
    }

    [Fact]
    public async Task PlaceOrder_CodTotalAboveLimit_Returns422CodNotAllowed()
    {
        await SetSettingsAsync(freeShippingThreshold: 500_000, codMaxAmount: 999_999);
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 1_000_000);
        var key = NewKey();

        var response = await PlaceAsync(anonymousId, key, BodyFor(expectedTotal: 1_000_000, paymentMethod: "cod"));

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "COD_NOT_ALLOWED");
        Assert.Equal(0, await OrderCountAsync(key));
        Assert.Equal(10, await _kit.StockOfAsync(skuId));
    }

    [Fact]
    public async Task PlaceOrder_CarrierUnavailable_Returns503AndChangesNothing()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);
        _factory.GhnClient.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Unavailable);
        var key = NewKey();

        var response = await PlaceAsync(anonymousId, key, BodyFor(total));

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, "SHIPPING_UNAVAILABLE");
        Assert.Equal(0, await OrderCountAsync(key));
        Assert.Equal(10, await _kit.StockOfAsync(skuId));
    }

    // ----- Tiêu chí 4: thiếu hàng -----

    [Fact]
    public async Task PlaceOrder_StockDropsAfterPricing_Returns409WithShortSkuAndChangesNothing()
    {
        var anonymousId = _kit.NewAnonymousId();
        var shortSkuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 3);
        var enoughSkuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 1);
        var total = await PreviewTotalAsync(anonymousId);
        var key = NewKey();
        var host = CreateHostWithCallbackAfterFee(() =>
            _kit.UpdateSkuAsync(shortSkuId, sku => sku.Stock = 1));
        var movementsBefore = await _kit.MovementCountOfAsync(enoughSkuId);

        var response = await SendOrderAsync(host.Client, anonymousId, key, BodyFor(total));

        var error = await CartTestKit.AssertErrorAsync(response, HttpStatusCode.Conflict, "INSUFFICIENT_STOCK");
        Assert.Equal([shortSkuId], DetailKeys(error));
        var message = error.GetProperty("details")[0].GetProperty("message").GetString()!;
        Assert.Contains("1", message);
        Assert.Equal(0, await OrderCountAsync(key));
        // Giữ hàng theo kiểu tất cả hoặc không: SKU đủ hàng cũng không bị trừ.
        Assert.Equal(1, await _kit.StockOfAsync(shortSkuId));
        Assert.Equal(10, await _kit.StockOfAsync(enoughSkuId));
        Assert.Equal(movementsBefore, await _kit.MovementCountOfAsync(enoughSkuId));
        Assert.Equal(2, await CartLineCountAsync(anonymousId));
        Assert.Empty(host.Events.OfType<OrderPlacedEvent>());
        Assert.Empty(host.Events.OfType<StockChangedEvent>());
    }

    [Fact]
    public async Task PlaceOrder_TwoSkusShort_ListsBothInDetails()
    {
        var anonymousId = _kit.NewAnonymousId();
        var firstSkuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 2);
        var secondSkuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 2);
        var total = await PreviewTotalAsync(anonymousId);
        var host = CreateHostWithCallbackAfterFee(async () =>
        {
            await _kit.UpdateSkuAsync(firstSkuId, sku => sku.Stock = 0);
            await _kit.UpdateSkuAsync(secondSkuId, sku => sku.Stock = 1);
        });

        var response = await SendOrderAsync(host.Client, anonymousId, NewKey(), BodyFor(total));

        var error = await CartTestKit.AssertErrorAsync(response, HttpStatusCode.Conflict, "INSUFFICIENT_STOCK");
        Assert.Equal(
            new[] { firstSkuId, secondSkuId }.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            DetailKeys(error).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    // ----- Tiêu chí 5: lỗi sau bước giữ hàng hoàn lại cả đơn lẫn tồn kho -----

    [Fact]
    public async Task PlaceOrder_FailureAfterStockReservation_RollsBackOrderStockCartAndEvents()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 2);
        var total = await PreviewTotalAsync(anonymousId);
        var key = NewKey();
        var movementsBefore = await _kit.MovementCountOfAsync(skuId);
        var stockEventsBefore = _factory.DomainEvents.OfType<StockChangedEvent>().Count(change => change.SkuId == skuId);

        HttpResponseMessage response;
        // Trigger ném lỗi khi xóa dòng giỏ. Bước xóa chạy sau bước giữ hàng, nên lỗi buộc rollback tồn kho đã trừ.
        await ExecuteSqlAsync("""
            CREATE OR REPLACE FUNCTION com08_fail_cart_item_delete() RETURNS trigger AS $$
            BEGIN RAISE EXCEPTION 'COM-08: ép lỗi sau khi giữ hàng'; END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER com08_fail_cart_item_delete BEFORE DELETE ON cart_items
            FOR EACH ROW EXECUTE FUNCTION com08_fail_cart_item_delete();
            """);
        try
        {
            response = await PlaceAsync(anonymousId, key, BodyFor(total));
        }
        finally
        {
            await ExecuteSqlAsync("""
                DROP TRIGGER IF EXISTS com08_fail_cart_item_delete ON cart_items;
                DROP FUNCTION IF EXISTS com08_fail_cart_item_delete();
                """);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await OrderCountAsync(key));
        Assert.Equal(10, await _kit.StockOfAsync(skuId));
        Assert.Equal(movementsBefore, await _kit.MovementCountOfAsync(skuId));
        var cart = Assert.Single(await _kit.CartsOfAnonymousAsync(anonymousId));
        var line = Assert.Single(cart.Items);
        Assert.Equal((skuId, 2), (line.SkuId, line.Quantity));
        Assert.DoesNotContain(_factory.DomainEvents.OfType<OrderPlacedEvent>(), placed => placed.AnonymousId == anonymousId);
        Assert.Equal(
            stockEventsBefore, _factory.DomainEvents.OfType<StockChangedEvent>().Count(change => change.SkuId == skuId));
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_kit.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    // ----- Tiêu chí 6: trạng thái ban đầu, snapshot và kết quả sau khi đặt -----

    [Fact]
    public async Task PlaceOrder_Cod_CreatesPendingConfirmationOrderWithoutDeadline()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId, "cod");

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total, "cod"));

        var data = await ReadDataAsync(response, HttpStatusCode.Created);
        Assert.Equal("pending_confirmation", data.GetProperty("status").GetString());
        Assert.Equal("unpaid", data.GetProperty("paymentStatus").GetString());
        Assert.Equal("cod", data.GetProperty("paymentMethod").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("paymentDeadline").ValueKind);
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        Assert.Equal(OrderStatus.PendingConfirmation, order.Status);
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal(PaymentMethod.Cod, order.PaymentMethod);
        Assert.Null(order.PaymentDeadline);
        Assert.Matches("^PK[A-HJ-NP-Z2-9]{8}$", order.Code);
    }

    [Fact]
    public async Task PlaceOrder_Sepay_CreatesPendingPaymentOrderWithThirtyMinuteDeadline()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId, "sepay");
        var before = DateTime.UtcNow;

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total, "sepay"));

        var after = DateTime.UtcNow;
        var data = await ReadDataAsync(response, HttpStatusCode.Created);
        Assert.Equal("pending_payment", data.GetProperty("status").GetString());
        Assert.Equal("unpaid", data.GetProperty("paymentStatus").GetString());
        Assert.Equal("sepay", data.GetProperty("paymentMethod").GetString());
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        // Hạn thanh toán bằng giờ tạo cộng 30 phút. Kiểm theo khoảng thời gian thật của request, nên không phụ thuộc lịch luồng.
        var deadline = Assert.NotNull(order.PaymentDeadline);
        Assert.InRange(deadline, before.AddMinutes(30), after.AddMinutes(30));
        // JSON ghi thời điểm tới mili giây, còn DB giữ tới micro giây. Hai giá trị lệch nhau không quá 1 mili giây.
        var responseDeadline = data.GetProperty("paymentDeadline").GetDateTime().ToUniversalTime();
        Assert.InRange(Math.Abs((deadline - responseDeadline).TotalMilliseconds), 0, 1);
    }

    [Fact]
    public async Task PlaceOrder_SavesSnapshotOfProductPriceImageAddressContactAndShipping()
    {
        // Tạm tính 600.000 đ vượt ngưỡng 500.000 đ nên shop chịu phí, và đơn lưu cả phí GHN lẫn giảm phí.
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 300_000, quantity: 2, imagePublicId: "pickory/test-image");
        var total = await PreviewTotalAsync(anonymousId);
        var invoice = new
        {
            companyName = "Công ty Pickory",
            taxCode = "0312345678-001",
            address = "1 Đường Hóa Đơn",
            email = "ketoan@example.com",
        };
        var body = BodyFor(total, invoice: invoice, note: "Giao giờ hành chính", phone: "+84 901 234 567");

        var response = await PlaceAsync(anonymousId, NewKey(), body);

        var data = await ReadDataAsync(response, HttpStatusCode.Created);
        var fee = FakeFee(weightGrams: 600, insuredValue: 600_000);
        Assert.Equal(600_000, data.GetProperty("subtotal").GetInt32());
        Assert.Equal(fee, data.GetProperty("shippingFee").GetInt32());
        Assert.Equal(fee, data.GetProperty("shippingDiscount").GetInt32());
        Assert.Equal(600_000, data.GetProperty("total").GetInt32());

        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        Assert.Equal(600_000, order.Subtotal);
        Assert.Equal(fee, order.ShippingFee);
        Assert.Equal(fee, order.ShippingDiscount);
        Assert.Equal(600_000, order.Total);
        Assert.Null(order.UserId);
        Assert.Equal(anonymousId, order.AnonymousId);

        var item = Assert.Single(order.Items);
        Assert.Equal(skuId, item.SkuId);
        Assert.Equal(300_000, item.UnitPrice);
        Assert.Equal(2, item.Quantity);
        Assert.StartsWith("Sản phẩm ", item.NameSnapshot);
        Assert.StartsWith("DOC-", item.SkuCodeSnapshot);
        Assert.Equal("pickory/test-image", item.ImagePublicId);
        Assert.Equal("Đỏ", item.OptionValues.RootElement.GetProperty("Màu").GetString());

        var address = order.ShippingAddress.RootElement;
        Assert.Equal(ProvinceCode, address.GetProperty("provinceCode").GetString());
        Assert.Equal(ProvinceName, address.GetProperty("provinceName").GetString());
        Assert.Equal(WardCode, address.GetProperty("wardCode").GetString());
        Assert.Equal(WardName, address.GetProperty("wardName").GetString());
        Assert.Equal("12 Phố Test", address.GetProperty("line").GetString());

        var contact = order.Contact.RootElement;
        Assert.Equal("Nguyễn Văn An", contact.GetProperty("name").GetString());
        Assert.Equal("0901234567", contact.GetProperty("phone").GetString());
        Assert.Equal("an@example.com", contact.GetProperty("email").GetString());
        Assert.Equal("Giao giờ hành chính", contact.GetProperty("note").GetString());

        var savedInvoice = order.InvoiceInfo!.RootElement;
        Assert.Equal("Công ty Pickory", savedInvoice.GetProperty("companyName").GetString());
        Assert.Equal("0312345678-001", savedInvoice.GetProperty("taxCode").GetString());
        Assert.Equal("1 Đường Hóa Đơn", savedInvoice.GetProperty("address").GetString());
        Assert.Equal("ketoan@example.com", savedInvoice.GetProperty("email").GetString());
    }

    [Fact]
    public async Task PlaceOrder_WithoutInvoice_StoresNullInvoiceInfo()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total));

        var data = await ReadDataAsync(response, HttpStatusCode.Created);
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        Assert.Null(order.InvoiceInfo);
    }

    [Fact]
    public async Task PlaceOrder_WritesOneFirstStatusHistoryRow()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId, "sepay");

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total, "sepay"));

        var data = await ReadDataAsync(response, HttpStatusCode.Created);
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        var history = Assert.Single(order.StatusHistory);
        Assert.Equal(OrderStatus.PendingPayment, history.ToStatus);
        Assert.Null(history.ActorId);
    }

    [Fact]
    public async Task PlaceOrder_ReservesStockAndRemovesOrderedLinesFromCart()
    {
        var anonymousId = _kit.NewAnonymousId();
        var firstSkuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 3, stock: 10);
        var secondSkuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 1, stock: 5);
        var total = await PreviewTotalAsync(anonymousId);

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total));

        await ReadDataAsync(response, HttpStatusCode.Created);
        Assert.Equal(7, await _kit.StockOfAsync(firstSkuId));
        Assert.Equal(4, await _kit.StockOfAsync(secondSkuId));
        Assert.Equal(0, await CartLineCountAsync(anonymousId));
    }

    [Fact]
    public async Task PlaceOrder_LoggedInUser_StoresUserIdOnOrder()
    {
        var (userId, sessionToken) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000, sessionToken: sessionToken);
        var total = await PreviewTotalAsync(anonymousId, sessionToken: sessionToken);

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total), sessionToken);

        var data = await ReadDataAsync(response, HttpStatusCode.Created);
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        Assert.Equal(userId, order.UserId);
        var placed = Assert.Single(_factory.DomainEvents.OfType<OrderPlacedEvent>(), item => item.Code == order.Code);
        Assert.Equal(userId, placed.UserId);
        Assert.Equal(order.Total, placed.Total);
        Assert.Equal(PaymentMethod.Cod, placed.PaymentMethod);
    }

    [Fact]
    public async Task PlaceOrder_EmptyCart_Returns400CartError()
    {
        var anonymousId = _kit.NewAnonymousId();

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(expectedTotal: 100_000));

        var error = await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(["cart"], DetailKeys(error));
    }

    // ----- Tiêu chí 7: kiểm dữ liệu liên hệ và hóa đơn -----

    [Theory]
    [InlineData("name", "", "contact.name")]
    [InlineData("phone", "", "contact.phone")]
    [InlineData("phone", "12345", "contact.phone")]
    [InlineData("email", "", "contact.email")]
    [InlineData("email", "khong-phai-email", "contact.email")]
    public async Task PlaceOrder_InvalidContact_Returns400WithFieldKey_AndChangesNothing(string field, string value, string expectedKey)
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);
        var key = NewKey();
        var body = field switch
        {
            "name" => BodyFor(total, name: value),
            "phone" => BodyFor(total, phone: value),
            _ => BodyFor(total, email: value),
        };

        var response = await PlaceAsync(anonymousId, key, body);

        var error = await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal([expectedKey], DetailKeys(error));
        Assert.Equal(0, await OrderCountAsync(key));
        Assert.Equal(10, await _kit.StockOfAsync(skuId));
    }

    [Fact]
    public async Task PlaceOrder_InvoiceWithBadTaxCode_Returns400OnTaxCodeField()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);
        var invoice = new { companyName = "Công ty", taxCode = "12345", address = "1 Đường", email = "kt@example.com" };

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total, invoice: invoice));

        var error = await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(["invoice.taxCode"], DetailKeys(error));
    }

    // ----- Tiêu chí 8: token xem đơn -----

    [Fact]
    public async Task PlaceOrder_ReturnsAccessTokenOnce_AndStoresOnlyItsHash()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);
        var key = NewKey();

        var response = await PlaceAsync(anonymousId, key, BodyFor(total));

        var data = await ReadDataAsync(response, HttpStatusCode.Created);
        var token = data.GetProperty("accessToken").GetString()!;
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        Assert.Equal(expectedHash, order.AccessTokenHash);
        Assert.NotEqual(token, order.AccessTokenHash);

        // Token gốc không nằm ở bất kỳ cột nào của đơn, cả trong cột jsonb.
        await using var connection = new NpgsqlConnection(_kit.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT row_to_json(o)::text FROM orders o WHERE o.code = @code", connection);
        command.Parameters.AddWithValue("code", order.Code);
        var rowText = (string)(await command.ExecuteScalarAsync())!;
        Assert.DoesNotContain(token, rowText);

        // Token gốc không nằm trong log của host test.
        var leaked = _factory.Logs.Entries.Where(entry =>
            entry.Message.Contains(token, StringComparison.Ordinal)
            || (entry.Exception?.Contains(token, StringComparison.Ordinal) ?? false));
        Assert.Empty(leaked);
    }

    // ----- Tiêu chí 9: sự kiện -----

    private sealed class CommittedStateProbe
    {
        public ConcurrentQueue<(bool OrderVisible, bool CartLineGone, int Stock)> Observations { get; } = new();
    }

    private sealed class OrderPlacedProbeHandler(IServiceScopeFactory scopes, CommittedStateProbe probe)
        : IDomainEventHandler<OrderPlacedEvent>
    {
        public async Task HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken = default)
        {
            // Scope mới có `DbContext` và kết nối riêng, nên chỉ đọc được dữ liệu đã commit.
            await using var scope = scopes.CreateAsyncScope();
            var commerce = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            var orderVisible = await commerce.Orders.AsNoTracking()
                .AnyAsync(order => order.Id == @event.OrderId, cancellationToken);
            var cartLineCount = await commerce.CartItems.AsNoTracking()
                .CountAsync(item => item.Cart!.AnonymousId == @event.AnonymousId, cancellationToken);
            var stock = await commerce.Database
                .SqlQuery<int>($"SELECT COALESCE(SUM(delta), 0)::int AS \"Value\" FROM inventory_movements WHERE \"orderId\" = {@event.OrderId}")
                .SingleAsync(cancellationToken);
            probe.Observations.Enqueue((orderVisible, cartLineCount == 0, stock));
        }
    }

    [Fact]
    public async Task PlaceOrder_PublishesExactlyOneOrderPlacedEventAfterCommit()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000, quantity: 2);
        var total = await PreviewTotalAsync(anonymousId);
        var probe = new CommittedStateProbe();
        var host = CreateHostWith(services =>
        {
            services.AddSingleton(probe);
            services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>, OrderPlacedProbeHandler>();
        });

        var response = await SendOrderAsync(host.Client, anonymousId, NewKey(), BodyFor(total));

        var data = await ReadDataAsync(response, HttpStatusCode.Created);
        var code = data.GetProperty("code").GetString()!;
        var placed = Assert.Single(host.Events.OfType<OrderPlacedEvent>());
        Assert.Equal(code, placed.Code);
        Assert.Equal(anonymousId, placed.AnonymousId);
        Assert.Equal(data.GetProperty("total").GetInt32(), placed.Total);
        // Lúc handler nhận sự kiện, đơn đã commit, dòng giỏ đã xóa và hàng đã được giữ (tổng delta của đơn là -2).
        var observation = Assert.Single(probe.Observations);
        Assert.True(observation.OrderVisible);
        Assert.True(observation.CartLineGone);
        Assert.Equal(-2, observation.Stock);
        // Sự kiện tồn kho của catalog đi qua cùng collector và cũng chỉ phát sau commit.
        var stockChange = Assert.Single(host.Events.OfType<StockChangedEvent>());
        Assert.Equal(skuId, stockChange.SkuId);
    }

    // ----- Tiêu chí 10: trùng mã đơn -----

    private sealed class ScriptedOrderCodeSource(IEnumerable<string> codes) : IOrderCodeSource
    {
        private readonly Queue<string> _codes = new(codes);

        public int CallCount { get; private set; }

        public string Next()
        {
            CallCount++;
            // Hết kịch bản thì trả mã cuối, để test thấy vòng thử lại dừng ở giới hạn thay vì chờ mãi.
            return _codes.Count > 1 ? _codes.Dequeue() : _codes.Peek();
        }
    }

    private async Task SeedExistingOrderAsync(string code)
    {
        var key = NewKey();
        await using var commerce = _kit.Database.CreateCommerceContext();
        commerce.Orders.Add(new Order
        {
            Code = code,
            AnonymousId = Guid.NewGuid().ToString(),
            Contact = JsonDocument.Parse("""{"name":"A","phone":"0901234567","email":"a@example.com","note":null}"""),
            ShippingAddress = JsonDocument.Parse("""{"provinceCode":"1","provinceName":"A","wardCode":"2","wardName":"B","line":"x"}"""),
            PaymentMethod = PaymentMethod.Cod,
            Status = OrderStatus.PendingConfirmation,
            PaymentStatus = PaymentStatus.Unpaid,
            Subtotal = 1,
            Total = 1,
            AccessTokenHash = "seed",
            IdempotencyKey = key,
            CreatedAt = DateTime.UtcNow,
        });
        await commerce.SaveChangesAsync();
    }

    [Fact]
    public async Task PlaceOrder_GeneratedCodeAlreadyExists_GeneratesAnotherCode()
    {
        await SeedExistingOrderAsync("PKAAAAAAAA");
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);
        var source = new ScriptedOrderCodeSource(["PKAAAAAAAA", "PKBBBBBBBB"]);
        var host = CreateHostWith(services => services.AddSingleton<IOrderCodeSource>(source));

        var response = await SendOrderAsync(host.Client, anonymousId, NewKey(), BodyFor(total));

        var data = await ReadDataAsync(response, HttpStatusCode.Created);
        Assert.Equal("PKBBBBBBBB", data.GetProperty("code").GetString());
        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public async Task PlaceOrder_EveryGeneratedCodeCollides_StopsAfterFiveAttemptsAndRollsBack()
    {
        await SeedExistingOrderAsync("PKAAAAAAAA");
        var anonymousId = _kit.NewAnonymousId();
        var skuId = await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);
        var source = new ScriptedOrderCodeSource(["PKAAAAAAAA"]);
        var host = CreateHostWith(services => services.AddSingleton<IOrderCodeSource>(source));
        var key = NewKey();

        var response = await SendOrderAsync(host.Client, anonymousId, key, BodyFor(total));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(5, source.CallCount);
        Assert.Equal(0, await OrderCountAsync(key));
        Assert.Equal(10, await _kit.StockOfAsync(skuId));
        Assert.Equal(1, await CartLineCountAsync(anonymousId));
    }

    [Fact]
    public async Task PlaceOrder_ManyOrders_CreateDistinctCodes()
    {
        var codes = new HashSet<string>();
        for (var index = 0; index < 5; index++)
        {
            _factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
            var anonymousId = _kit.NewAnonymousId();
            await AddToCartAsync(anonymousId, price: 100_000);
            var total = await PreviewTotalAsync(anonymousId);

            var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total));

            var data = await ReadDataAsync(response, HttpStatusCode.Created);
            codes.Add(data.GetProperty("code").GetString()!);
        }

        Assert.Equal(5, codes.Count);
        Assert.Equal(5, await OrderCountForKeysAsync());
    }
}
