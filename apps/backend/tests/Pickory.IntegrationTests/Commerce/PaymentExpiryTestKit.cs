using System.Net;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Configuration;
using Pickory.Shared.Events;

namespace Pickory.IntegrationTests.Commerce;

// Dùng đơn tạo qua API để kiểm việc trả và giữ lại kho thật, không giả lập Catalog.
internal sealed class PaymentExpiryTestKit(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database) : IAsyncDisposable
{
    public const string ApiKey = "expiry-test-key";
    public const string Account = "1017588888";
    public static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 0, 0, TimeSpan.Zero);
    private readonly CartTestKit _cart = new(factory, database);
    private readonly List<string> _keys = [];
    private readonly List<string> _providerIds = [];
    private readonly List<WebApplicationFactory<Program>> _hosts = [];
    public FakeTimeProvider Clock { get; } = new(Now);
    public IntegrationDatabaseFixture Database => database;
    public ExpiryEventObserver Events { get; } = new(database);

    public async Task InitializeAsync()
    {
        // Cấu hình shipping và tài khoản riêng giữ test độc lập với môi trường phát triển.
        await _cart.InitializeAsync();
        await QuoteTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
        factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
        await _cart.SetLimitsAsync(5, 20);
        await using var commerce = database.CreateCommerceContext();
        var settings = await commerce.Settings.SingleAsync();
        settings.SepayAccountNumber = Account;
        await commerce.SaveChangesAsync();
    }

    public WebApplicationFactory<Program> CreateHost(Action<IServiceCollection>? configure = null)
    {
        // Đồng hồ giả và observer chỉ thay trong host test, không tạo seam production.
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var config = (AppConfig)services.Single(row => row.ServiceType == typeof(AppConfig)).ImplementationInstance!;
            services.RemoveAll<AppConfig>();
            services.AddSingleton(config with { SepayWebhookApiKey = ApiKey });
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IDomainEventHandler<OrderStatusChangedEvent>>(Events);
            services.AddSingleton<IDomainEventHandler<OrderPaidEvent>>(Events);
            configure?.Invoke(services);
        }));
        _hosts.Add(host);
        host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        return host;
    }

    public async Task<ExpiryOrder> CreateOrderAsync(string method = "sepay", DateTime? deadline = null)
    {
        var anonymousId = _cart.NewAnonymousId();
        var product = await _cart.AddProductAsync(stock: 10);
        // Giỏ, preview và đặt đơn thật tạo movement giữ hai đơn vị hàng.
        Xunit.Assert.Equal(HttpStatusCode.OK, (await _cart.AddAsync(anonymousId, product.SkuIds[0], 2)).StatusCode);
        var address = new { provinceCode = "1000001", wardCode = "1003646", line = "12 Phố Test" };
        var preview = await _cart.SendAsync(HttpMethod.Post, "/api/checkout/previews", anonymousId, new { address, paymentMethod = method });
        var total = (await CartTestKit.ReadCartAsync(preview)).GetProperty("total").GetInt32();
        var key = "expiry-" + Guid.NewGuid().ToString("N");
        _keys.Add(key);
        var response = await _cart.SendAsync(HttpMethod.Post, "/api/orders", anonymousId,
            new { address, paymentMethod = method, expectedTotal = total, contact = new { name = "Nguyễn Văn An", phone = "0901234567", email = "an@example.com" } },
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = key });
        Xunit.Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var token = (await CartTestKit.ReadJsonAsync(response)).GetProperty("data").GetProperty("accessToken").GetString()!;
        await using var commerce = database.CreateCommerceContext();
        var order = await commerce.Orders.SingleAsync(row => row.IdempotencyKey == key);
        // Chỉ thay hạn để kiểm biên bằng đồng hồ giả, không chờ ba mươi phút thật.
        order.PaymentDeadline = deadline ?? Now.UtcDateTime.AddSeconds(-1);
        await commerce.SaveChangesAsync();
        return new ExpiryOrder(order.Id, order.Code, product.SkuIds[0], total, anonymousId, token);
    }

    public async Task<Order> ReadOrderAsync(string id)
    {
        await using var commerce = database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().Include(row => row.StatusHistory).SingleAsync(row => row.Id == id);
    }

    public async Task<int> StockAsync(string skuId)
    {
        await using var catalog = database.CreateCatalogContext();
        return await catalog.Skus.Where(row => row.Id == skuId).Select(row => row.Stock).SingleAsync();
    }

    public async Task CancelAsync(ExpiryOrder order)
    {
        // Route tự hủy kiểm quyền bằng token thật lấy từ phản hồi đặt đơn.
        var response = await _cart.SendAsync(HttpMethod.Post, $"/api/orders/{order.Code}/cancellation?t={order.Token}", order.AnonymousId);
        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public Dictionary<string, object?> Payload(ExpiryOrder order, int? amount = null)
    {
        var id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
        _providerIds.Add(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new Dictionary<string, object?>
        {
            ["id"] = id, ["gateway"] = "Vietcombank", ["transactionDate"] = "2030-01-02 03:00:00",
            ["accountNumber"] = Account, ["subAccount"] = "", ["code"] = null, ["content"] = order.Code,
            ["transferType"] = "in", ["description"] = "", ["transferAmount"] = amount ?? order.Total,
            ["accumulated"] = 100_000_000, ["referenceCode"] = "FT123",
        };
    }

    public static async Task SendAsync(HttpClient client, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sepay");
        request.Headers.TryAddWithoutValidation("Authorization", "Apikey " + ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        Xunit.Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Xunit.Assert.Equal("{\"success\": true}", await response.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        // Đóng host trước khi dọn dữ liệu để không còn request giữ khóa của test.
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
        await using var commerce = database.CreateCommerceContext();
        var ids = await commerce.Orders.Where(row => _keys.Contains(row.IdempotencyKey)).Select(row => row.Id).ToListAsync();
        // Xóa chứng từ trước đơn để tôn trọng khóa ngoại khi dọn dữ liệu riêng của test.
        await commerce.PaymentTransactions.Where(row => _providerIds.Contains(row.ProviderTxId)).ExecuteDeleteAsync();
        // Xóa lịch sử và dòng hàng trước đơn để không vi phạm khóa ngoại.
        await commerce.OrderStatusHistory.Where(row => ids.Contains(row.OrderId)).ExecuteDeleteAsync();
        await commerce.OrderItems.Where(row => ids.Contains(row.OrderId)).ExecuteDeleteAsync();
        await commerce.Orders.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync();
        // Dọn giỏ, Catalog và shipping sau khi mọi tham chiếu từ đơn đã xóa.
        await _cart.CleanupAsync();
        await QuoteTestSetup.ResetAsync(factory);
    }

    internal sealed record ExpiryOrder(string Id, string Code, string SkuId, int Total, string AnonymousId, string Token);
}

// Mỗi sự kiện đọc qua kết nối khác để chứng minh đơn và kho đã commit cùng nhau.
internal sealed class ExpiryEventObserver(IntegrationDatabaseFixture database)
    : IDomainEventHandler<OrderStatusChangedEvent>, IDomainEventHandler<OrderPaidEvent>
{
    public ConcurrentQueue<ExpiryObservation> Observations { get; } = new();
    public TaskCompletionSource Changed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task HandleAsync(OrderStatusChangedEvent value, CancellationToken cancellationToken = default) => ObserveAsync(value.OrderId, false, cancellationToken);
    public Task HandleAsync(OrderPaidEvent value, CancellationToken cancellationToken = default) => ObserveAsync(value.OrderId, true, cancellationToken);

    private async Task ObserveAsync(string orderId, bool paid, CancellationToken cancellationToken)
    {
        // Context riêng không thấy thay đổi chưa commit của processor hoặc webhook.
        await using var commerce = database.CreateCommerceContext();
        var order = await commerce.Orders.AsNoTracking().Include(row => row.Items).SingleAsync(row => row.Id == orderId, cancellationToken);
        // Kho đọc qua kết nối Catalog riêng chứng minh hàng đã commit cùng trạng thái đơn.
        await using var catalog = database.CreateCatalogContext();
        var skuId = order.Items.Single().SkuId;
        var stock = await catalog.Skus.Where(row => row.Id == skuId).Select(row => row.Stock).SingleAsync(cancellationToken);
        // Số lịch sử bảo vệ việc phát sự kiện trước khi lý do chuyển đơn được lưu.
        var history = await commerce.OrderStatusHistory.CountAsync(row => row.OrderId == orderId, cancellationToken);
        Observations.Enqueue(new ExpiryObservation(orderId, paid, order.Status, order.PaymentStatus, stock, history));
        // Báo hoàn tất sau khi đọc đủ dữ liệu để test timer không tranh với observer.
        Changed.TrySetResult();
    }
    internal sealed record ExpiryObservation(string OrderId, bool PaidEvent, OrderStatus Status, PaymentStatus PaymentStatus, int Stock, int HistoryCount);
}
