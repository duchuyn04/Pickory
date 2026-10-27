using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Shipping.Entities;
using Pickory.Modules.Shipping.FallbackFees;
using Pickory.Modules.Shipping.Ghn;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-27: đơn ghi lại nguồn phí vận chuyển (GHN, phí đã lưu, phí dự phòng), còn khách không thấy nguồn phí ở route nào.
// Kho seed ở Hà Nội (miền Bắc) và địa chỉ nhận ở Hồ Chí Minh (miền Nam), nên tuyến là liên vùng.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class PlaceOrderFeeSourceTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string OrdersPath = "/api/orders";
    private const string PreviewPath = "/api/checkout/previews";
    private const string LookupPath = "/api/order-lookups";
    private const string WebOrigin = "http://localhost:5173";
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";

    // Tên trường mà khách không được thấy. Hằng số này giúp test không phụ thuộc cách viết hoa.
    private const string FeeSourceProperty = "shippingFeeSource";

    // Phí liên vùng của bảng dự phòng, độc lập với cân nặng: khối lượng gốc 20.000 g và giá bước 0.
    private const int FallbackCrossRegionFee = 35_000;

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];

    public PlaceOrderFeeSourceTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
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
        await SetSettingsAsync(freeShippingThreshold: 500_000);
        _factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
        _factory.GhnClient.ClearFailures();
    }

    public async Task DisposeAsync()
    {
        _factory.GhnClient.ClearFailures();
        await using (var commerce = _kit.Database.CreateCommerceContext())
        {
            var orderIds = await commerce.Orders
                .Where(order => _keys.Contains(order.IdempotencyKey))
                .Select(order => order.Id)
                .ToListAsync();
            await commerce.OrderStatusHistory.Where(history => orderIds.Contains(history.OrderId)).ExecuteDeleteAsync();
            await commerce.OrderItems.Where(item => orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
            await commerce.Orders.Where(order => orderIds.Contains(order.Id)).ExecuteDeleteAsync();
        }

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

    private static string NewEmail() => $"khach-{Guid.NewGuid():N}@example.com";

    private static Dictionary<string, object?> BodyFor(int expectedTotal, string email) =>
        new()
        {
            ["contact"] = new { name = "Nguyễn Văn An", phone = "0901234567", email },
            ["address"] = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            ["paymentMethod"] = "cod",
            ["expectedTotal"] = expectedTotal,
        };

    private Task<HttpResponseMessage> PlaceAsync(
        string anonymousId, string key, object body, string? sessionToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, OrdersPath);
        var cookie = $"pk_anon={anonymousId}";
        if (sessionToken is not null)
        {
            cookie += $"; pk_session={sessionToken}";
        }

        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", WebOrigin);
        request.Headers.Add("Idempotency-Key", key);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        return _client.SendAsync(request);
    }

    private async Task<string> AddToCartAsync(string anonymousId, int price, string? sessionToken = null)
    {
        var product = await _kit.AddProductAsync(price: price, stock: 10);
        var skuId = product.SkuIds[0];
        var response = await _kit.AddAsync(anonymousId, skuId, 1, sessionToken: sessionToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return skuId;
    }

    private Task<HttpResponseMessage> PreviewAsync(string anonymousId, string? sessionToken = null)
    {
        var body = new
        {
            address = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            paymentMethod = "cod",
        };
        return _kit.SendAsync(HttpMethod.Post, PreviewPath, anonymousId, body, sessionToken);
    }

    private async Task<int> PreviewTotalAsync(string anonymousId, string? sessionToken = null)
    {
        var response = await PreviewAsync(anonymousId, sessionToken);
        var data = await CartTestKit.ReadCartAsync(response);
        return data.GetProperty("total").GetInt32();
    }

    private async Task<JsonElement> ReadCreatedAsync(HttpResponseMessage response)
    {
        var json = await CartTestKit.ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Mong đợi 201 nhưng nhận {(int)response.StatusCode}: {json}");
        return json.GetProperty("data");
    }

    private async Task<Order> LoadOrderAsync(string code)
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().SingleAsync(order => order.Code == code);
    }

    private async Task SetSettingsAsync(int freeShippingThreshold)
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        commerce.Settings.Add(new CommerceSettings
        {
            Id = "default",
            FreeShippingThreshold = freeShippingThreshold,
            CodMaxAmount = 50_000_000,
            MaxQuantityPerSku = 5,
            MaxCartLines = 20,
            UpdatedAt = DateTime.UtcNow,
        });
        await commerce.SaveChangesAsync();
    }

    // Bảng phí dự phòng đủ chín giá. Tuyến liên vùng luôn bằng `FallbackCrossRegionFee`, bất kể cân nặng của kiện.
    private async Task SeedFallbackTableAsync(bool enabled)
    {
        await using var shipping = _kit.Database.CreateShippingContext();
        shipping.FallbackFees.Add(new ShippingFallbackFee
        {
            Id = FallbackFeeStore.SingletonId,
            Enabled = enabled,
            SameProvinceBaseFee = 10_000,
            SameProvinceBaseWeightGrams = 20_000,
            SameProvinceStepFee = 0,
            SameRegionBaseFee = 20_000,
            SameRegionBaseWeightGrams = 20_000,
            SameRegionStepFee = 0,
            CrossRegionBaseFee = FallbackCrossRegionFee,
            CrossRegionBaseWeightGrams = 20_000,
            CrossRegionStepFee = 0,
            UpdatedAt = DateTime.UtcNow,
        });
        await shipping.SaveChangesAsync();
    }

    private void GhnIsDown() =>
        _factory.GhnClient.SetFailure(FakeGhnOperation.CalculateFee, FakeGhnFailure.Unavailable);

    // Duyệt cây JSON tìm thuộc tính theo tên, không phân biệt hoa thường.
    private static bool ContainsProperty(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                        || ContainsProperty(property.Value, name))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(item => ContainsProperty(item, name));
            default:
                return false;
        }
    }

    // ----- Nguồn phí ghi trên đơn -----

    [Fact]
    public async Task PlaceOrder_GhnAnswers_StoresGhnSource()
    {
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total, NewEmail()));

        var data = await ReadCreatedAsync(response);
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        Assert.Equal(ShippingFeeSource.Ghn, order.ShippingFeeSource);
    }

    [Fact]
    public async Task PlaceOrder_GhnDownAndFallbackEnabled_CodOrderIsCreatedWithTableFeeAndFallbackSource()
    {
        await SeedFallbackTableAsync(enabled: true);
        GhnIsDown();
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var total = await PreviewTotalAsync(anonymousId);

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total, NewEmail()));

        var data = await ReadCreatedAsync(response);
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        Assert.Equal(ShippingFeeSource.Fallback, order.ShippingFeeSource);
        Assert.Equal(PaymentMethod.Cod, order.PaymentMethod);
        Assert.Equal(OrderStatus.PendingConfirmation, order.Status);
        // Tạm tính 100.000 đ chưa tới ngưỡng 500.000 đ, nên khách trả đủ phí theo bảng.
        Assert.Equal(FallbackCrossRegionFee, order.ShippingFee);
        Assert.Equal(0, order.ShippingDiscount);
        Assert.Equal(100_000 + FallbackCrossRegionFee, order.Total);
        Assert.Equal(total, order.Total);
    }

    [Fact]
    public async Task PlaceOrder_GhnDownAndSubtotalAboveFreeShippingThreshold_AppliesDiscountLikeGhnFee()
    {
        await SeedFallbackTableAsync(enabled: true);
        GhnIsDown();
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 600_000);
        var total = await PreviewTotalAsync(anonymousId);

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(total, NewEmail()));

        var data = await ReadCreatedAsync(response);
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        Assert.Equal(ShippingFeeSource.Fallback, order.ShippingFeeSource);
        // Phí theo bảng vẫn được ghi, và ngưỡng miễn phí giảm đúng bằng phí đó như với phí GHN.
        Assert.Equal(FallbackCrossRegionFee, order.ShippingFee);
        Assert.Equal(FallbackCrossRegionFee, order.ShippingDiscount);
        Assert.Equal(600_000, order.Total);
    }

    [Fact]
    public async Task PlaceOrder_GhnDownWithSavedFee_StoresCachedSourceAndTheSavedFee()
    {
        // Bảng phí dự phòng bật nhưng phí đã lưu đi trước, nên đơn phải ghi nguồn `cached`.
        await SeedFallbackTableAsync(enabled: true);
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var ghnTotal = await PreviewTotalAsync(anonymousId);
        GhnIsDown();
        var cachedTotal = await PreviewTotalAsync(anonymousId);

        var response = await PlaceAsync(anonymousId, NewKey(), BodyFor(cachedTotal, NewEmail()));

        var data = await ReadCreatedAsync(response);
        var order = await LoadOrderAsync(data.GetProperty("code").GetString()!);
        Assert.Equal(ShippingFeeSource.Cached, order.ShippingFeeSource);
        Assert.Equal(ghnTotal, order.Total);
        Assert.NotEqual(FallbackCrossRegionFee, order.ShippingFee);
    }

    [Fact]
    public async Task PlaceOrder_GhnDownAndFallbackDisabled_Returns503AndCreatesNoOrder()
    {
        await SeedFallbackTableAsync(enabled: false);
        GhnIsDown();
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000);
        var key = NewKey();

        var response = await PlaceAsync(anonymousId, key, BodyFor(135_000, NewEmail()));

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, "SHIPPING_UNAVAILABLE");
        await using var commerce = _kit.Database.CreateCommerceContext();
        Assert.Equal(0, await commerce.Orders.CountAsync(order => order.IdempotencyKey == key));
    }

    // ----- Khách không thấy nguồn phí -----

    [Fact]
    public async Task CustomerRoutes_ForFallbackOrder_NeverExposeTheFeeSource()
    {
        await SeedFallbackTableAsync(enabled: true);
        GhnIsDown();
        var (_, sessionToken) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        await AddToCartAsync(anonymousId, price: 100_000, sessionToken);
        var email = NewEmail();

        // Xem trước thanh toán.
        var previewResponse = await PreviewAsync(anonymousId, sessionToken);
        var preview = await CartTestKit.ReadCartAsync(previewResponse);
        Assert.False(ContainsProperty(preview, FeeSourceProperty), $"Preview lộ nguồn phí: {preview}");

        // Đặt đơn.
        var total = preview.GetProperty("total").GetInt32();
        var placed = await ReadCreatedAsync(await PlaceAsync(anonymousId, NewKey(), BodyFor(total, email), sessionToken));
        Assert.False(ContainsProperty(placed, FeeSourceProperty), $"Đặt đơn lộ nguồn phí: {placed}");
        var code = placed.GetProperty("code").GetString()!;
        var token = placed.GetProperty("accessToken").GetString()!;
        Assert.Equal(ShippingFeeSource.Fallback, (await LoadOrderAsync(code)).ShippingFeeSource);

        // Đơn của tôi.
        var list = await GetJsonAsync(OrdersPath, anonymousId, sessionToken);
        Assert.False(ContainsProperty(list, FeeSourceProperty), $"Danh sách đơn lộ nguồn phí: {list}");

        // Chi tiết đơn bằng token.
        var detail = await GetJsonAsync($"{OrdersPath}/{code}?t={Uri.EscapeDataString(token)}", anonymousId);
        Assert.False(ContainsProperty(detail, FeeSourceProperty), $"Chi tiết đơn lộ nguồn phí: {detail}");

        // Tra cứu đơn bằng mã đơn và email. Route trả token để mở đơn ngay, không trả nguồn phí.
        var lookupRequest = new HttpRequestMessage(HttpMethod.Post, LookupPath);
        lookupRequest.Headers.Add("Origin", WebOrigin);
        lookupRequest.Content = new StringContent(
            JsonSerializer.Serialize(new { orderCode = code, email }), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        var lookupResponse = await _client.SendAsync(lookupRequest);
        var lookup = await CartTestKit.ReadJsonAsync(lookupResponse);
        Assert.Equal(HttpStatusCode.OK, lookupResponse.StatusCode);
        Assert.False(ContainsProperty(lookup, FeeSourceProperty), $"Tra cứu đơn lộ nguồn phí: {lookup}");

        // Thư xác nhận đơn không nhắc tới phí dự phòng. Tra cứu không còn gửi thư riêng.
        var messages = _factory.EmailSender.MessagesTo(email);
        Assert.Single(messages);
        Assert.All(messages, message =>
        {
            Assert.DoesNotContain("dự phòng", message.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fallback", message.Text, StringComparison.OrdinalIgnoreCase);
        });
    }

    private async Task<JsonElement> GetJsonAsync(string path, string anonymousId, string? sessionToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        var cookie = $"pk_anon={anonymousId}";
        if (sessionToken is not null)
        {
            cookie += $"; pk_session={sessionToken}";
        }

        request.Headers.Add("Cookie", cookie);
        var response = await _client.SendAsync(request);
        var json = await CartTestKit.ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} trả {(int)response.StatusCode}: {json}");
        return json;
    }
}
