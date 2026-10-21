using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Commerce;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.ActivityTracking.Counters;
using Pickory.Modules.ActivityTracking.Persistence;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Shared.Calendar;
using Pickory.Shared.Configuration;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.ActivityTracking;

// ACT-07: hai handler cộng `add_to_cart` và `order_placed` khi Commerce phát sự kiện. Test đi qua API giỏ và API đặt đơn thật rồi đọc `daily_counters`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CommerceCounterHandlersTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string OrdersPath = "/api/orders";
    private const string PreviewPath = "/api/checkout/previews";
    private const string WebOrigin = "http://localhost:5173";
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";
    private const string SepayApiKey = "ACT07-secret-not-for-logs";
    private const string SepayAccount = "1017588888";

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];
    private readonly List<string> _codes = [];
    private readonly List<string> _providerIds = [];
    private readonly List<WebApplicationFactory<Program>> _hosts = [];
    // Test không dùng đồng hồ giả, nên đọc bộ đếm bằng hiệu trước và sau. Ngày chốt một lần ở đây để hai lần đọc luôn cùng khoảng ngày.
    private readonly DateOnly _today = VietnamCalendar.ToVietnamDate(DateTime.UtcNow);
    // Log của bộ phát nằm chung cả class. Test chỉ xét log phát sinh từ vị trí này trở đi.
    private int _logsBefore;

    public CommerceCounterHandlersTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new CartTestKit(factory, database);
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync()
    {
        _logsBefore = _factory.Logs.Entries.Count;
        await _kit.InitializeAsync();
        await QuoteTestSetup.ResetAsync(_factory);
        await QuoteTestSetup.SeedCatalogAsync(_factory);
        await QuoteTestSetup.SeedSettingsAsync(_factory, defaultServiceTypeId: 2);
        // Bộ đếm preview là singleton của factory. Xóa để lượt preview của test trước không chặn test này.
        _factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
    }

    public async Task DisposeAsync()
    {
        // Đóng host dựng thêm trước khi xóa dữ liệu, để không còn request giữ khóa DB.
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        await using (var identity = _database.CreateIdentityContext())
        {
            await identity.AuditLogs
                .Where(log => log.TargetType == "order" && _codes.Contains(log.TargetId))
                .ExecuteDeleteAsync();
        }

        await using (var commerce = _database.CreateCommerceContext())
        {
            var orderIds = await commerce.Orders.Where(order => _keys.Contains(order.IdempotencyKey))
                .Select(order => order.Id).ToListAsync();
            // Xóa giao dịch trước đơn vì khóa ngoại chặn xóa đơn còn giao dịch.
            await commerce.PaymentTransactions.Where(row => _providerIds.Contains(row.ProviderTxId)).ExecuteDeleteAsync();
            await commerce.OrderStatusHistory.Where(history => orderIds.Contains(history.OrderId)).ExecuteDeleteAsync();
            await commerce.OrderItems.Where(item => orderIds.Contains(item.OrderId)).ExecuteDeleteAsync();
            await commerce.Orders.Where(order => orderIds.Contains(order.Id)).ExecuteDeleteAsync();
        }

        await _kit.CleanupAsync();
        await QuoteTestSetup.ResetAsync(_factory);
    }

    // ----- Thêm giỏ: tiêu chí 1 và 2 -----

    [Fact]
    public async Task AddToCart_WithQuantityThree_IncrementsAddToCartByOne()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        var before = await ReadCountersAsync();

        var response = await _kit.AddAsync(anonymousId, skuId, quantity: 3);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await ReadCountersAsync();
        // Số lượng của dòng không đổi cách đếm: chỉ số đếm lượt thêm, không đếm món.
        Assert.Equal(before.AddToCart + 1, after.AddToCart);
        Assert.Equal(before.OrderPlaced, after.OrderPlaced);
        Assert.Equal(before.ProductView, after.ProductView);
    }

    [Fact]
    public async Task AddToCart_SameSkuAgain_IncrementsAddToCartAgain()
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        var before = await ReadCountersAsync();

        var first = await _kit.AddAsync(anonymousId, skuId);
        var afterFirst = await ReadCountersAsync();
        var second = await _kit.AddAsync(anonymousId, skuId);
        var afterSecond = await ReadCountersAsync();

        // Lần hai đi qua nhánh cộng dồn vào dòng có sẵn, và vẫn phát sự kiện.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(before.AddToCart + 1, afterFirst.AddToCart);
        Assert.Equal(before.AddToCart + 2, afterSecond.AddToCart);
        var cart = await CartTestKit.ReadCartAsync(second);
        Assert.Equal(2, CartTestKit.LineOf(cart, skuId).GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task AddToCart_RejectedByQuantityLimit_KeepsCounters()
    {
        await _kit.SetLimitsAsync(maxQuantityPerSku: 2, maxCartLines: 7);
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        var before = await ReadCountersAsync();

        var response = await _kit.AddAsync(anonymousId, skuId, quantity: 3);

        // Giỏ từ chối thì không có sự kiện, nên bộ đếm không đổi.
        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "CART_LIMIT_EXCEEDED");
        Assert.Equal(before, await ReadCountersAsync());
    }

    [Fact]
    public async Task AddToCart_SameSkuPushesTotalOverLimit_KeepsAddToCartAtOne()
    {
        await _kit.SetLimitsAsync(maxQuantityPerSku: 2, maxCartLines: 7);
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        var before = await ReadCountersAsync();

        var first = await _kit.AddAsync(anonymousId, skuId, quantity: 2);
        var afterFirst = await ReadCountersAsync();
        // Tổng 2 + 1 vượt giới hạn 2. Giỏ từ chối trong khóa, sau khi dòng cũ đã có.
        var second = await _kit.AddAsync(anonymousId, skuId, quantity: 1);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await CartTestKit.AssertErrorAsync(second, HttpStatusCode.UnprocessableEntity, "CART_LIMIT_EXCEEDED");
        Assert.Equal(before.AddToCart + 1, afterFirst.AddToCart);
        Assert.Equal(afterFirst, await ReadCountersAsync());
    }

    // ----- Đặt đơn: tiêu chí 3 và 4 -----

    [Fact]
    public async Task PlaceOrder_Cod_IncrementsOrderPlacedByOneAndKeepsAddToCart()
    {
        var draft = await PrepareOrderAsync("cod");
        // Đọc sau bước thêm giỏ của `PrepareOrderAsync`. Bước đặt đơn không được cộng `add_to_cart`.
        var before = await ReadCountersAsync();

        var response = await SendOrderAsync(_client, draft);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = await ReadCountersAsync();
        Assert.Equal(before.OrderPlaced + 1, after.OrderPlaced);
        Assert.Equal(before.AddToCart, after.AddToCart);
        Assert.Equal(before.ProductView, after.ProductView);
    }

    [Fact]
    public async Task PlaceOrder_Sepay_IncrementsOrderPlacedByOne()
    {
        var draft = await PrepareOrderAsync("sepay");
        var before = await ReadCountersAsync();

        var response = await SendOrderAsync(_client, draft);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var after = await ReadCountersAsync();
        Assert.Equal(before.OrderPlaced + 1, after.OrderPlaced);
        Assert.Equal(before.AddToCart, after.AddToCart);
    }

    [Fact]
    public async Task PlaceOrder_SameIdempotencyKeyTwice_IncrementsOrderPlacedOnce()
    {
        var draft = await PrepareOrderAsync("cod");
        var before = await ReadCountersAsync();

        var first = await SendOrderAsync(_client, draft);
        var second = await SendOrderAsync(_client, draft);

        // Lần gửi lại trả đơn cũ và không phát sự kiện mới, nên bộ đếm chỉ tăng một lần.
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var after = await ReadCountersAsync();
        Assert.Equal(before.OrderPlaced + 1, after.OrderPlaced);
    }

    // ----- Đơn được trả tiền: tiêu chí 5 -----

    [Fact]
    public async Task SepayWebhookPaysOrder_KeepsAllCounters()
    {
        await SetSepayAccountAsync();
        var placed = await PlaceOrderAsync("sepay");
        var before = await ReadCountersAsync();
        var (client, events) = CreateSepayHost();
        var payload = SepayPayload("MBVCB." + placed.Code.ToLowerInvariant() + ".FT123", placed.Total);

        var response = await SendWebhookAsync(client, payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Chứng minh sự kiện đã phát: nếu đơn chưa được trả thì việc bộ đếm không đổi không nói lên điều gì.
        Assert.Equal(PaymentStatus.Paid, await PaymentStatusOfAsync(placed.Code));
        Assert.Single(events.OfType<OrderPaidEvent>(), paid => paid.Code == placed.Code);
        Assert.Equal(before, await ReadCountersAsync());
    }

    [Fact]
    public async Task AdminConfirmsCodReceipt_KeepsAllCounters()
    {
        var placed = await PlaceOrderAsync("cod");
        await SetOrderDeliveredAsync(placed.Code);
        var (_, session) = await IdentityTestData.CreateStaffSessionAsync(_database, "sales", "act07");
        var before = await ReadCountersAsync();

        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Put, $"/api/admin/orders/{placed.Code}/cod-receipt", null, $"pk_session={session}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Đơn COD đã được đếm lúc đặt. Nếu `OrderPaidEvent` cũng cộng `order_placed` thì đơn bị đếm hai lần.
        Assert.Equal(PaymentStatus.Paid, await PaymentStatusOfAsync(placed.Code));
        Assert.Single(_factory.DomainEvents.OfType<OrderPaidEvent>(), paid => paid.Code == placed.Code);
        Assert.Equal(before, await ReadCountersAsync());
    }

    // ----- Ghi bộ đếm lỗi: tiêu chí 6 -----

    [Fact]
    public async Task AddToCart_CounterDatabaseUnreachable_StillSucceedsAndKeepsCartLine()
    {
        var unreachable = new UnreachableDatabaseInterceptor();
        var client = CreateHostClientWithUnreachableCounterDatabase(unreachable);
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];

        var response = await SendJsonAsync(client, HttpMethod.Post, "/api/cart/items", anonymousId, new { skuId, quantity = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Giỏ đã commit trước khi phát sự kiện, nên dòng hàng còn trong DB dù bộ đếm lỗi.
        var carts = await _kit.CartsOfAnonymousAsync(anonymousId);
        var line = Assert.Single(Assert.Single(carts).Items);
        Assert.Equal(skuId, line.SkuId);
        Assert.Equal(2, line.Quantity);
        AssertDispatcherLoggedFailure(unreachable, nameof(CartItemAddedEvent));
    }

    [Fact]
    public async Task PlaceOrder_CounterDatabaseUnreachable_StillCreatesOrder()
    {
        var unreachable = new UnreachableDatabaseInterceptor();
        var client = CreateHostClientWithUnreachableCounterDatabase(unreachable);
        var draft = await PrepareOrderAsync("cod");

        var response = await SendOrderAsync(client, draft);

        // Ghi nhớ mã đơn trước mọi assert. Assert đỏ vẫn để `DisposeAsync` dọn được đơn này.
        var json = await CartTestKit.ReadJsonAsync(response);
        if (json.TryGetProperty("data", out var data))
        {
            _codes.Add(data.GetProperty("code").GetString()!);
        }

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Đơn đã commit trước khi phát sự kiện, nên đơn còn trong DB dù bộ đếm lỗi.
        Assert.Equal(1, await OrderCountAsync(draft.Key));
        AssertDispatcherLoggedFailure(unreachable, nameof(OrderPlacedEvent));
    }

    // ----- Token đã hủy: quyết định 4 -----

    [Fact]
    public async Task CartItemAddedHandler_WithCanceledToken_StillIncrementsAddToCart()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IDomainEventHandler<CartItemAddedEvent>>()
            .OfType<CartItemAddedCounterHandler>().Single();
        var before = await ReadCountersAsync();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await handler.HandleAsync(new CartItemAddedEvent("sku-1", 3, null, Guid.NewGuid().ToString(), null), canceled.Token);

        // Token của request bị hủy sau khi giỏ đã commit không được làm mất lượt thêm giỏ.
        var after = await ReadCountersAsync();
        Assert.Equal(before.AddToCart + 1, after.AddToCart);
    }

    [Fact]
    public async Task OrderPlacedHandler_WithCanceledToken_StillIncrementsOrderPlaced()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IDomainEventHandler<OrderPlacedEvent>>()
            .OfType<OrderPlacedCounterHandler>().Single();
        var before = await ReadCountersAsync();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        var orderPlaced = new OrderPlacedEvent("order-1", "PKTEST0001", null, Guid.NewGuid().ToString(), 100_000, PaymentMethod.Cod);
        await handler.HandleAsync(orderPlaced, canceled.Token);

        // Token của request bị hủy sau khi đơn đã commit không được làm mất lượt đặt đơn.
        var after = await ReadCountersAsync();
        Assert.Equal(before.OrderPlaced + 1, after.OrderPlaced);
    }

    // ----- Dựng dữ liệu và gửi request -----

    private sealed record Counters(long AddToCart, long OrderPlaced, long ProductView);

    private sealed record OrderDraft(string AnonymousId, string Key, Dictionary<string, object?> Body);

    private sealed record PlacedOrder(string Code, int Total);

    // Cộng hôm nay và ngày kế tiếp. Ngày Việt Nam có thể đổi giữa lúc test chạy, và tổng hai ngày vẫn tăng đúng bằng số lượt.
    private async Task<Counters> ReadCountersAsync()
    {
        await using var context = _database.CreateActivityTrackingContext();
        var rows = await context.Database
            .SqlQuery<MetricTotal>(
                $"SELECT metric AS \"Metric\", SUM(value)::bigint AS \"Total\" FROM daily_counters WHERE date >= {_today} AND date <= {_today.AddDays(1)} GROUP BY metric")
            .ToListAsync();
        long TotalOf(string metric) => rows.SingleOrDefault(row => row.Metric == metric)?.Total ?? 0;
        return new Counters(TotalOf("add_to_cart"), TotalOf("order_placed"), TotalOf("product_view"));
    }

    private sealed record MetricTotal(string Metric, long Total);

    // Hàm thêm một sản phẩm vào giỏ bằng route giỏ thật. Hàm dựng body đặt đơn từ tổng tiền của preview, không gửi đơn.
    private async Task<OrderDraft> PrepareOrderAsync(string paymentMethod)
    {
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync(price: 100_000)).SkuIds[0];
        Assert.Equal(HttpStatusCode.OK, (await _kit.AddAsync(anonymousId, skuId)).StatusCode);

        var address = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" };
        var preview = await _kit.SendAsync(HttpMethod.Post, PreviewPath, anonymousId, new { address, paymentMethod });
        var total = (await CartTestKit.ReadCartAsync(preview)).GetProperty("total").GetInt32();

        var key = "act07-" + Guid.NewGuid().ToString("N");
        _keys.Add(key);
        var body = new Dictionary<string, object?>
        {
            ["contact"] = new { name = "Nguyễn Văn An", phone = "0901234567", email = "an@example.com" },
            ["address"] = address,
            ["paymentMethod"] = paymentMethod,
            ["expectedTotal"] = total,
        };
        return new OrderDraft(anonymousId, key, body);
    }

    private static async Task<HttpResponseMessage> SendOrderAsync(HttpClient client, OrderDraft draft)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, OrdersPath);
        request.Headers.Add("Cookie", $"pk_anon={draft.AnonymousId}");
        request.Headers.Add("Origin", WebOrigin);
        request.Headers.Add("Idempotency-Key", draft.Key);
        request.Content = new StringContent(
            JsonSerializer.Serialize(draft.Body), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        return await client.SendAsync(request);
    }

    private async Task<PlacedOrder> PlaceOrderAsync(string paymentMethod)
    {
        var draft = await PrepareOrderAsync(paymentMethod);
        var response = await SendOrderAsync(_client, draft);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await CartTestKit.ReadJsonAsync(response)).GetProperty("data");
        var code = data.GetProperty("code").GetString()!;
        _codes.Add(code);
        return new PlacedOrder(code, data.GetProperty("total").GetInt32());
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client, HttpMethod method, string path, string anonymousId, object body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"pk_anon={anonymousId}");
        request.Headers.Add("Origin", WebOrigin);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        return await client.SendAsync(request);
    }

    private async Task<int> OrderCountAsync(string key)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Orders.CountAsync(order => order.IdempotencyKey == key);
    }

    private async Task<PaymentStatus> PaymentStatusOfAsync(string code)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().Where(order => order.Code == code)
            .Select(order => order.PaymentStatus).SingleAsync();
    }

    // Đưa đơn COD sang `delivered` trực tiếp trong DB. Nhận tiền COD chỉ hợp lệ với đơn đã giao.
    private async Task SetOrderDeliveredAsync(string code)
    {
        await using var commerce = _database.CreateCommerceContext();
        var order = await commerce.Orders.SingleAsync(row => row.Code == code);
        order.Status = OrderStatus.Delivered;
        await commerce.SaveChangesAsync();
    }

    // ----- SePay -----

    // Cấu hình tài khoản nhận riêng của test, để webhook khớp đơn không phụ thuộc cấu hình máy chạy.
    private async Task SetSepayAccountAsync()
    {
        await using var commerce = _database.CreateCommerceContext();
        commerce.Settings.Add(new CommerceSettings
        {
            Id = "default", FreeShippingThreshold = 500_000, CodMaxAmount = 20_000_000,
            MaxQuantityPerSku = 5, MaxCartLines = 20, SepayAccountNumber = SepayAccount,
            UpdatedAt = DateTime.UtcNow,
        });
        await commerce.SaveChangesAsync();
    }

    // Host dựng thêm có danh sách sự kiện riêng, nên trả kèm để test đọc `OrderPaidEvent` của đúng host này.
    private (HttpClient Client, RecordedDomainEvents Events) CreateSepayHost()
    {
        var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Thay `AppConfig` đã nạp bằng bản có khóa webhook của test. Host gốc để trống khóa này.
            var config = (AppConfig)services.Single(row => row.ServiceType == typeof(AppConfig)).ImplementationInstance!;
            services.RemoveAll<AppConfig>();
            services.AddSingleton(config with { SepayWebhookApiKey = SepayApiKey });
        }));
        _hosts.Add(host);
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return (client, host.Services.GetRequiredService<RecordedDomainEvents>());
    }

    private Dictionary<string, object?> SepayPayload(string content, int amount)
    {
        // ID ngẫu nhiên giữ test độc lập với thứ tự chạy.
        var id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
        _providerIds.Add(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new Dictionary<string, object?>
        {
            ["id"] = id, ["gateway"] = "Vietcombank", ["transactionDate"] = "2026-10-12 10:00:00",
            ["accountNumber"] = SepayAccount, ["subAccount"] = "", ["code"] = null,
            ["content"] = content, ["transferType"] = "in", ["description"] = "",
            ["transferAmount"] = amount, ["accumulated"] = 105_000_000, ["referenceCode"] = "FT123",
        };
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(HttpClient client, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sepay");
        // SePay là máy gọi máy, nên request không có `Origin`.
        request.Headers.TryAddWithoutValidation("Authorization", "Apikey " + SepayApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    // ----- DB bộ đếm không kết nối được -----

    // Ném lỗi ngay lúc mở kết nối, đúng như khi DB không kết nối được. Cách này ổn định hơn trỏ tới cổng không có DB, vì không chờ timeout mạng.
    private sealed class UnreachableDatabaseInterceptor : DbConnectionInterceptor
    {
        private int _openAttempts;

        public int OpenAttempts => Volatile.Read(ref _openAttempts);

        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            Interlocked.Increment(ref _openAttempts);
            throw new InvalidOperationException("Mô phỏng DB bộ đếm không kết nối được.");
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _openAttempts);
            throw new InvalidOperationException("Mô phỏng DB bộ đếm không kết nối được.");
        }
    }

    // Host dựng thêm chỉ đổi `ActivityTrackingDbContext`. Giỏ và đơn của Commerce vẫn dùng DB thật.
    private HttpClient CreateHostClientWithUnreachableCounterDatabase(UnreachableDatabaseInterceptor interceptor)
    {
        var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<ActivityTrackingDbContext>(options => options.AddInterceptors(interceptor))));
        _hosts.Add(host);
        return host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    // Handler đã chạm vào DB hỏng, và bộ phát đã bắt lỗi rồi ghi log. Hai điều kiện này chứng minh luồng thành công không phải do handler không chạy.
    private void AssertDispatcherLoggedFailure(UnreachableDatabaseInterceptor interceptor, string eventName)
    {
        Assert.True(interceptor.OpenAttempts >= 1, "Handler phải đã thử mở kết nối tới DB bộ đếm.");
        Assert.Contains(_factory.Logs.Entries.Skip(_logsBefore), entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains(eventName, StringComparison.Ordinal)
            && entry.Exception is not null
            && entry.Exception.Contains("Mô phỏng DB bộ đếm không kết nối được.", StringComparison.Ordinal));
    }
}
