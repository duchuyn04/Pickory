using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Shared.Configuration;
using Pickory.Shared.Email;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-09: thư xác nhận gửi sau commit, chỉ khi đơn mới được tạo, lỗi gửi chỉ ghi log và log không chứa token xem đơn.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed partial class OrderConfirmationEmailTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string OrdersPath = "/api/orders";
    private const string PreviewPath = "/api/checkout/previews";
    private const string WebOrigin = "http://localhost:5173";
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];
    private readonly List<WebApplicationFactory<Program>> _derivedFactories = [];
    private int _logsBefore;

    public OrderConfirmationEmailTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
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
        _factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
        _factory.GhnClient.ClearFailures();
        // Chỉ xét log phát sinh trong test này. Các test khác của lớp dùng chung bộ gom log của factory.
        _logsBefore = _factory.Logs.Entries.Count;
    }

    public async Task DisposeAsync()
    {
        foreach (var derived in _derivedFactories)
        {
            derived.Dispose();
        }

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

    // Mỗi test dùng email riêng. Hộp thư trong bộ nhớ dùng chung giữa các test của factory.
    private static string NewEmail() => $"khach-{Guid.NewGuid():N}@example.com";

    private static Dictionary<string, object?> BodyFor(int expectedTotal, string email) =>
        new()
        {
            ["contact"] = new { name = "Nguyễn Văn An", phone = "0901234567", email },
            ["address"] = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            ["paymentMethod"] = "cod",
            ["expectedTotal"] = expectedTotal,
        };

    private static Task<HttpResponseMessage> SendOrderAsync(
        HttpClient client, string anonymousId, string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, OrdersPath);
        request.Headers.Add("Cookie", $"pk_anon={anonymousId}");
        request.Headers.Add("Origin", WebOrigin);
        request.Headers.Add("Idempotency-Key", key);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        return client.SendAsync(request);
    }

    private async Task<(string AnonymousId, string SkuId, int Total)> PrepareCartAsync(int price = 100_000, int quantity = 2)
    {
        var anonymousId = _kit.NewAnonymousId();
        var product = await _kit.AddProductAsync(price: price, stock: 10);
        var skuId = product.SkuIds[0];
        var added = await _kit.AddAsync(anonymousId, skuId, quantity);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        var preview = new
        {
            address = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            paymentMethod = "cod",
        };
        var response = await _kit.SendAsync(HttpMethod.Post, PreviewPath, anonymousId, preview);
        var data = await CartTestKit.ReadCartAsync(response);
        return (anonymousId, skuId, data.GetProperty("total").GetInt32());
    }

    private async Task<JsonElement> ReadCreatedAsync(HttpResponseMessage response)
    {
        var json = await CartTestKit.ReadJsonAsync(response);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Mong đợi 201 nhưng nhận {(int)response.StatusCode}: {json}");
        return json.GetProperty("data");
    }

    private async Task<Order?> FindOrderAsync(string code)
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.Code == code);
    }

    private static string TokenInLink(string text)
    {
        var match = LinkPattern().Match(text);
        Assert.True(match.Success, $"Thân thư không có liên kết xem đơn: {text}");
        return Uri.UnescapeDataString(match.Groups["token"].Value);
    }

    [GeneratedRegex(@"/orders/(?<code>[^?\s]+)\?t=(?<token>[^\s]+)")]
    private static partial Regex LinkPattern();

    private IEnumerable<string> LogLinesOfThisTest() =>
        _factory.Logs.Entries.Skip(_logsBefore).SelectMany(entry => new[] { entry.Message, entry.Exception ?? string.Empty });

    private HttpClient CreateHostWith(Action<IServiceCollection> configure)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(configure));
        _derivedFactories.Add(derived);
        return derived.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    // Sender giả luôn ném lỗi. Chỉ host dựng riêng của test dùng nó, nên test khác không thấy lỗi.
    private sealed class ThrowingEmailSender : IEmailSender
    {
        public const string FailureText = "Máy chủ thư không phản hồi.";

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(FailureText);
    }

    // Sender giả đọc đơn bằng một DbContext mới ngay lúc gửi. Đọc được nghĩa là đơn đã commit trước khi gửi thư.
    private sealed class ProbingEmailSender(
        InMemoryEmailSender inner, IntegrationDatabaseFixture database) : IEmailSender
    {
        // Mỗi lần gửi ghi một dòng, kể cả khi đơn chưa đọc được. Test kiểm mọi lần gửi đều thấy đơn.
        public List<(string Code, bool OrderFound, int ItemCount)> Sends { get; } = [];

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            await using var commerce = database.CreateCommerceContext();
            var code = message.Metadata["orderCode"];
            var order = await commerce.Orders.AsNoTracking().Include(item => item.Items)
                .SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
            Sends.Add((code, order is not null, order?.Items.Count ?? 0));

            await inner.SendAsync(message, cancellationToken);
        }
    }

    // Bọc client GHN để chạy hàm của test ngay sau khi báo phí, sau lúc server đã tính tiền.
    private sealed class CallbackAfterFeeGhnClient(IGhnClient inner, Func<Task> afterFee) : IGhnClient
    {
        private int _called;

        public async Task<GhnFee> CalculateFeeAsync(GhnFeeRequest request, CancellationToken cancellationToken)
        {
            var fee = await inner.CalculateFeeAsync(request, cancellationToken);
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

        public Task<GhnOrderPreview> PreviewOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken) =>
            inner.PreviewOrderAsync(request, cancellationToken);

        public Task<GhnCreatedOrder> CreateOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken) =>
            inner.CreateOrderAsync(request, cancellationToken);

        public Task CancelOrderAsync(string orderCode, string reasonCode, string? reason, CancellationToken cancellationToken) =>
            inner.CancelOrderAsync(orderCode, reasonCode, reason, cancellationToken);

        public Task<GhnOrderDetail> GetOrderDetailAsync(string orderCode, CancellationToken cancellationToken) =>
            inner.GetOrderDetailAsync(orderCode, cancellationToken);
    }

    // ----- Tiêu chí: gửi đúng thư -----

    [Fact]
    public async Task PlaceOrder_Success_SendsOneEmailToContactWithLinkAndResponseToken()
    {
        var (anonymousId, _, total) = await PrepareCartAsync();
        var email = NewEmail();

        var response = await SendOrderAsync(_client, anonymousId, NewKey(), BodyFor(total, email));

        var data = await ReadCreatedAsync(response);
        var code = data.GetProperty("code").GetString()!;
        var accessToken = data.GetProperty("accessToken").GetString()!;
        var messages = _factory.EmailSender.MessagesTo(email);
        var message = Assert.Single(messages);
        Assert.Equal($"Pickory: xác nhận đơn hàng {code}", message.Subject);
        Assert.Equal("order-confirmation", message.Metadata["purpose"]);
        Assert.Equal(code, message.Metadata["orderCode"]);
        var webOrigin = _factory.Services.GetRequiredService<AppConfig>().WebOrigin;
        Assert.Contains($"{webOrigin}/orders/{Uri.EscapeDataString(code)}?t=", message.Text);
        Assert.Equal(accessToken, TokenInLink(message.Text));
        Assert.Contains("Tổng tiền:", message.Text);
    }

    [Fact]
    public async Task PlaceOrder_Success_TokenInEmailLinkMatchesStoredHash()
    {
        var (anonymousId, _, total) = await PrepareCartAsync();
        var email = NewEmail();

        var response = await SendOrderAsync(_client, anonymousId, NewKey(), BodyFor(total, email));

        var data = await ReadCreatedAsync(response);
        var order = await FindOrderAsync(data.GetProperty("code").GetString()!);
        var token = TokenInLink(Assert.Single(_factory.EmailSender.MessagesTo(email)).Text);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        Assert.Equal(expectedHash, order!.AccessTokenHash);
    }

    [Fact]
    public async Task PlaceOrder_Success_SendsEmailOnlyAfterOrderIsCommitted()
    {
        var (anonymousId, _, total) = await PrepareCartAsync();
        var email = NewEmail();
        ProbingEmailSender? probe = null;
        var client = CreateHostWith(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(provider =>
                probe = new ProbingEmailSender(
                    provider.GetRequiredService<InMemoryEmailSender>(), _kit.Database));
        });

        var response = await SendOrderAsync(client, anonymousId, NewKey(), BodyFor(total, email));

        var data = await ReadCreatedAsync(response);
        var code = data.GetProperty("code").GetString()!;
        // Đơn đọc được qua DbContext mới ngay lúc gửi, nên đơn đã commit trước khi thư đi.
        Assert.NotNull(probe);
        var seen = Assert.Single(probe!.Sends);
        Assert.Equal(code, seen.Code);
        Assert.True(seen.OrderFound);
        Assert.Equal(1, seen.ItemCount);
    }

    // ----- Tiêu chí: không gửi thừa -----

    [Fact]
    public async Task PlaceOrder_SameKeyAgain_DoesNotSendASecondEmail()
    {
        var (anonymousId, _, total) = await PrepareCartAsync();
        var email = NewEmail();
        var key = NewKey();

        var first = await SendOrderAsync(_client, anonymousId, key, BodyFor(total, email));
        await ReadCreatedAsync(first);
        var replay = await SendOrderAsync(_client, anonymousId, key, BodyFor(total, email));

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Single(_factory.EmailSender.MessagesTo(email));
    }

    [Fact]
    public async Task PlaceOrder_ConcurrentRequestsWithSameKey_SendExactlyOneEmail()
    {
        var (anonymousId, _, total) = await PrepareCartAsync();
        var email = NewEmail();
        var key = NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => SendOrderAsync(_client, anonymousId, key, BodyFor(total, email))));

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, response => Assert.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"Nhận {(int)response.StatusCode}"));
        Assert.Single(_factory.EmailSender.MessagesTo(email));
    }

    [Fact]
    public async Task PlaceOrder_TotalChanged_SendsNoEmail()
    {
        var (anonymousId, _, total) = await PrepareCartAsync();
        var email = NewEmail();

        var response = await SendOrderAsync(_client, anonymousId, NewKey(), BodyFor(total + 1, email));

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.Conflict, "ORDER_TOTAL_CHANGED");
        Assert.Empty(_factory.EmailSender.MessagesTo(email));
    }

    [Fact]
    public async Task PlaceOrder_InsufficientStock_SendsNoEmail()
    {
        var (anonymousId, skuId, total) = await PrepareCartAsync(quantity: 3);
        var email = NewEmail();
        var client = CreateHostWith(services =>
        {
            services.RemoveAll<IGhnClient>();
            services.AddSingleton<IGhnClient>(provider => new CallbackAfterFeeGhnClient(
                provider.GetRequiredService<FakeGhnClient>(),
                () => _kit.UpdateSkuAsync(skuId, sku => sku.Stock = 1)));
        });

        var response = await SendOrderAsync(client, anonymousId, NewKey(), BodyFor(total, email));

        await CartTestKit.AssertErrorAsync(response, HttpStatusCode.Conflict, "INSUFFICIENT_STOCK");
        Assert.Empty(_factory.EmailSender.MessagesTo(email));
    }

    // ----- Tiêu chí: lỗi gửi thư chỉ ghi log -----

    [Fact]
    public async Task PlaceOrder_EmailSenderThrows_Returns201KeepsOrderClearsCartAndLogsOneError()
    {
        var (anonymousId, _, total) = await PrepareCartAsync();
        var email = NewEmail();
        var key = NewKey();
        var client = CreateHostWith(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, ThrowingEmailSender>();
        });

        var response = await SendOrderAsync(client, anonymousId, key, BodyFor(total, email));

        var data = await ReadCreatedAsync(response);
        var code = data.GetProperty("code").GetString()!;
        Assert.False(string.IsNullOrEmpty(data.GetProperty("accessToken").GetString()));
        Assert.NotNull(await FindOrderAsync(code));
        var carts = await _kit.CartsOfAnonymousAsync(anonymousId);
        Assert.Equal(0, carts.Sum(cart => cart.Items.Count));

        var errors = _factory.Logs.Entries.Skip(_logsBefore)
            .Where(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error && entry.Message.Contains(code))
            .ToList();
        var error = Assert.Single(errors);
        Assert.Contains(ThrowingEmailSender.FailureText, error.Exception);
    }

    // ----- Tiêu chí: log không chứa token -----

    [Fact]
    public async Task PlaceOrder_SuccessAndFailure_LogsContainNeitherTokenNorLink()
    {
        var (successAnonymousId, _, successTotal) = await PrepareCartAsync();
        var successEmail = NewEmail();
        var successResponse = await SendOrderAsync(
            _client, successAnonymousId, NewKey(), BodyFor(successTotal, successEmail));
        var successToken = (await ReadCreatedAsync(successResponse)).GetProperty("accessToken").GetString()!;

        var (failureAnonymousId, _, failureTotal) = await PrepareCartAsync();
        var failingClient = CreateHostWith(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, ThrowingEmailSender>();
        });
        var failureResponse = await SendOrderAsync(
            failingClient, failureAnonymousId, NewKey(), BodyFor(failureTotal, NewEmail()));
        var failureToken = (await ReadCreatedAsync(failureResponse)).GetProperty("accessToken").GetString()!;

        var lines = LogLinesOfThisTest().ToList();
        Assert.NotEmpty(lines);
        Assert.DoesNotContain(lines, line => line.Contains(successToken));
        Assert.DoesNotContain(lines, line => line.Contains(failureToken));
        Assert.DoesNotContain(lines, line => line.Contains("?t="));
    }
}
