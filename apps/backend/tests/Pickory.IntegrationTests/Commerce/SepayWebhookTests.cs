using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.IntegrationTests.Shipping;
using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Configuration;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

[Collection(IntegrationDatabaseCollection.Name)]
public sealed class SepayWebhookTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string ApiKey = "COM11-secret-not-for-logs";
    private const string Account = "1017588888";
    private readonly CheckoutWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly List<string> _keys = [];
    private readonly List<string> _providerIds = [];
    private readonly List<WebApplicationFactory<Program>> _hosts = [];

    public SepayWebhookTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new CartTestKit(factory, database);
    }

    public async Task InitializeAsync()
    {
        // Catalog và shipping thật cung cấp đầu vào cho đơn tạo bằng API, không dùng đơn giả.
        await _kit.InitializeAsync();
        await QuoteTestSetup.ResetAsync(_factory);
        await QuoteTestSetup.SeedCatalogAsync(_factory);
        await QuoteTestSetup.SeedSettingsAsync(_factory, defaultServiceTypeId: 2);
        _factory.Services.GetRequiredService<CheckoutPreviewRateLimiter>().Clear();
        await using var commerce = _database.CreateCommerceContext();
        // Cấu hình tài khoản nhận riêng của test tránh phụ thuộc cấu hình máy chạy.
        commerce.Settings.Add(new CommerceSettings
        {
            Id = "default", FreeShippingThreshold = 500_000, CodMaxAmount = 20_000_000,
            MaxQuantityPerSku = 5, MaxCartLines = 20, SepayAccountNumber = Account,
            UpdatedAt = DateTime.UtcNow,
        });
        await commerce.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        // Đóng host trước khi xóa dữ liệu để không còn request giữ khóa DB.
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
        await using var commerce = _database.CreateCommerceContext();
        var orderIds = await commerce.Orders.Where(row => _keys.Contains(row.IdempotencyKey))
            .Select(row => row.Id).ToListAsync();
        // Xóa chứng từ trước đơn vì khóa ngoại cố ý chặn xóa đơn có giao dịch.
        await commerce.PaymentTransactions.Where(row => _providerIds.Contains(row.ProviderTxId)).ExecuteDeleteAsync();
        await commerce.OrderStatusHistory.Where(row => orderIds.Contains(row.OrderId)).ExecuteDeleteAsync();
        await commerce.OrderItems.Where(row => orderIds.Contains(row.OrderId)).ExecuteDeleteAsync();
        await commerce.Orders.Where(row => orderIds.Contains(row.Id)).ExecuteDeleteAsync();
        await _kit.CleanupAsync();
        await QuoteTestSetup.ResetAsync(_factory);
    }

    private (HttpClient Client, SepayPaidEventObserver Events, SepayWebhookTransactionProbe Probe, TestServer Server) CreateHost(string key = ApiKey)
    {
        var events = new SepayPaidEventObserver(_database);
        var probe = new SepayWebhookTransactionProbe(_database, events);
        // Interceptor chỉ tồn tại trong host test, nên production không có seam gây lỗi.
        var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var config = (AppConfig)services.Single(row => row.ServiceType == typeof(AppConfig)).ImplementationInstance!;
            services.RemoveAll<AppConfig>();
            services.AddSingleton(config with { SepayWebhookApiKey = key });
            services.AddSingleton<IDomainEventHandler<OrderPaidEvent>>(events);
            services.AddDbContext<CommerceDbContext>(options => options.AddInterceptors(probe));
        }));
        _hosts.Add(host);
        return (host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }), events, probe, host.Server);
    }

    private async Task<Order> CreateOrderAsync(string paymentMethod = "sepay")
    {
        var anonymousId = _kit.NewAnonymousId();
        var product = await _kit.AddProductAsync();
        // Route giỏ thật bảo đảm checkout giữ hàng và tạo snapshot như người mua.
        Assert.Equal(HttpStatusCode.OK, (await _kit.AddAsync(anonymousId, product.SkuIds[0])).StatusCode);
        var address = new { provinceCode = "1000001", wardCode = "1003646", line = "12 Phố Test" };
        var preview = await _kit.SendAsync(HttpMethod.Post, "/api/checkout/previews", anonymousId, new { address, paymentMethod });
        var total = (await CartTestKit.ReadCartAsync(preview)).GetProperty("total").GetInt32();
        var key = "COM11-" + Guid.NewGuid().ToString("N");
        _keys.Add(key);
        // Đơn được tạo qua API với tổng từ preview, không chèn trực tiếp vào DB.
        var response = await _kit.SendAsync(HttpMethod.Post, "/api/orders", anonymousId,
            new { address, paymentMethod, expectedTotal = total, contact = new { name = "Nguyễn Văn An", phone = "0901234567", email = "an@example.com" } },
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = key });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().SingleAsync(row => row.IdempotencyKey == key);
    }

    private Dictionary<string, object?> Payload(string content = "khong co ma", int amount = 100_000)
    {
        // ID ngẫu nhiên giữ test độc lập, kể cả khi thứ tự chạy thay đổi.
        var id = BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
        _providerIds.Add(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new Dictionary<string, object?>
        {
            ["id"] = id, ["gateway"] = "Vietcombank", ["transactionDate"] = "2026-10-12 10:00:00",
            ["accountNumber"] = Account, ["subAccount"] = "", ["code"] = null,
            ["content"] = content, ["transferType"] = "in", ["description"] = "",
            ["transferAmount"] = amount, ["accumulated"] = 105_000_000, ["referenceCode"] = "FT123",
        };
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, object body,
        string? authorization = "Apikey " + ApiKey, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sepay");
        // Không gửi `Origin` vì SePay là máy gọi máy, không phải trình duyệt.
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }
        request.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, contentType);
        return await client.SendAsync(request);
    }

    private static async Task AssertSuccessAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Status {(int)response.StatusCode}: {body}");
        Assert.Equal("{\"success\": true}", body);
        Assert.DoesNotContain(ApiKey, body);
    }

    private async Task<PaymentTransaction> ReadTransactionAsync(Dictionary<string, object?> payload)
    {
        var id = Convert.ToString(payload["id"], System.Globalization.CultureInfo.InvariantCulture)!;
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.PaymentTransactions.AsNoTracking().SingleAsync(row => row.ProviderTxId == id);
    }

    private async Task<Order> ReadOrderAsync(string id)
    {
        await using var commerce = _database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().Include(row => row.StatusHistory).SingleAsync(row => row.Id == id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Apikey wrong")]
    [InlineData("Bearer " + ApiKey)]
    [InlineData("Apikey  " + ApiKey)]
    public async Task Unauthorized_DoesNotWriteDatabase(string? authorization)
    {
        var host = CreateHost();
        await using var commerce = _database.CreateCommerceContext();
        var before = await commerce.PaymentTransactions.CountAsync();
        // `HttpClient` chuẩn hóa khoảng trắng của `Authorization`, nên gửi header thô qua `TestServer` để giữ đúng đầu vào.
        var response = await host.Server.SendAsync(context =>
        {
            context.Request.Method = "POST";
            context.Request.Path = "/api/webhooks/sepay";
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Payload())));
            if (authorization is not null)
            {
                context.Request.Headers.Authorization = authorization;
            }
        });
        Assert.Equal(401, response.Response.StatusCode);
        using var document = await JsonDocument.ParseAsync(response.Response.Body);
        Assert.Equal("UNAUTHORIZED", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, await commerce.PaymentTransactions.CountAsync());
    }

    [Fact]
    public async Task MissingServerKey_RejectsEvenConfiguredClientHeader()
    {
        var host = CreateHost("");
        var payload = Payload();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(host.Client, payload)).StatusCode);
        await using var commerce = _database.CreateCommerceContext();
        Assert.False(await commerce.PaymentTransactions.AnyAsync());
    }

    [Theory]
    [InlineData(413, "oversize", "text/plain")]
    [InlineData(415, "{}", "text/plain")]
    [InlineData(400, "{", "application/json")]
    public async Task InvalidBody_RejectsBeforeDatabase(int status, string body, string contentType)
    {
        var host = CreateHost();
        // Dữ liệu ASCII có số byte chính xác 16 KB cộng một byte.
        if (body == "oversize")
        {
            body = new string('x', 16 * 1024 + 1);
        }
        var response = await SendAsync(host.Client, body, contentType: contentType);
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 400)
        {
            await CartTestKit.AssertErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
        await using var commerce = _database.CreateCommerceContext();
        Assert.False(await commerce.PaymentTransactions.AnyAsync());
    }

    [Theory]
    [InlineData("gateway", null)]
    [InlineData("id", "wrong")]
    [InlineData("transferAmount", 0L)]
    [InlineData("transferAmount", -1L)]
    [InlineData("transferAmount", 2147483648L)]
    public async Task InvalidPayload_ReturnsValidationError(string field, object? value)
    {
        var host = CreateHost();
        var payload = Payload();
        payload[field] = value;
        await CartTestKit.AssertErrorAsync(await SendAsync(host.Client, payload), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await using var commerce = _database.CreateCommerceContext();
        Assert.False(await commerce.PaymentTransactions.AnyAsync());
    }

    [Fact]
    public async Task WebhookWithoutOrigin_AcceptsButCartWriteStillRequiresOrigin()
    {
        var host = CreateHost();
        await AssertSuccessAsync(await SendAsync(host.Client, Payload()));
        var response = await _kit.SendAsync(HttpMethod.Post, "/api/cart/items", _kit.NewAnonymousId(),
            new { skuId = "missing", quantity = 1 }, withOrigin: false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("out", Account)]
    [InlineData("other", Account)]
    [InlineData("in", "wrong-account")]
    public async Task IneligibleTransfer_IsStoredIgnoredWithoutChangingOrder(string transferType, string accountNumber)
    {
        var order = await CreateOrderAsync();
        var host = CreateHost();
        var payload = Payload(order.Code, order.Total);
        payload["transferType"] = transferType;
        payload["accountNumber"] = accountNumber;
        await AssertSuccessAsync(await SendAsync(host.Client, payload));
        var transaction = await ReadTransactionAsync(payload);
        using (transaction.Raw)
        {
            Assert.Equal("ignored", transaction.MatchStatus);
            Assert.Null(transaction.OrderId);
        }
        Assert.Equal(OrderStatus.PendingPayment, (await ReadOrderAsync(order.Id)).Status);
        Assert.Empty(host.Events.Observations);
    }

    [Fact]
    public async Task MissingAccountConfiguration_IsIgnored()
    {
        await using var commerce = _database.CreateCommerceContext();
        await commerce.Settings.ExecuteDeleteAsync();
        var host = CreateHost();
        var payload = Payload();
        await AssertSuccessAsync(await SendAsync(host.Client, payload));
        var transaction = await ReadTransactionAsync(payload);
        using (transaction.Raw)
        {
            Assert.Equal("ignored", transaction.MatchStatus);
            Assert.Null(transaction.OrderId);
        }
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(-1, "underpaid")]
    [InlineData(1, "overpaid")]
    public async Task PendingOrder_MatchesIndividualAmountAndEmitsAfterCommit(int difference, string flag)
    {
        var order = await CreateOrderAsync();
        var host = CreateHost();
        var payload = Payload("MBVCB." + order.Code.ToLowerInvariant() + ".FT123", order.Total + difference);
        host.Probe.Arm();
        var timer = Stopwatch.StartNew();
        var response = await SendAsync(host.Client, payload);
        timer.Stop();
        await AssertSuccessAsync(response);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"Elapsed: {timer.Elapsed}");
        var saved = await ReadOrderAsync(order.Id);
        var transaction = await ReadTransactionAsync(payload);
        using (transaction.Raw)
        {
            Assert.Equal("sepay", transaction.Provider);
            Assert.Equal(order.Total + difference, transaction.Amount);
            Assert.Equal(payload["content"], transaction.Content);
            Assert.Equal("matched", transaction.MatchStatus);
            Assert.Equal(order.Id, transaction.OrderId);
            // JSONB đổi thứ tự thuộc tính, nên so giá trị thay vì chuỗi JSON.
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(payload), transaction.Raw.RootElement));
            Assert.DoesNotContain(ApiKey, transaction.Raw.RootElement.GetRawText());
        }
        var paid = difference >= 0;
        Assert.Equal(paid ? OrderStatus.PendingConfirmation : OrderStatus.PendingPayment, saved.Status);
        Assert.Equal(paid ? PaymentStatus.Paid : PaymentStatus.Unpaid, saved.PaymentStatus);
        Assert.Equal(flag == "" ? [] : new[] { flag }, saved.Flags);
        Assert.Equal(paid ? 1 : 0, saved.StatusHistory.Count(row => row.ToStatus == OrderStatus.PendingConfirmation));
        Assert.Equal(paid ? 1 : 0, host.Events.Observations.Count);
        Assert.Equal(0, host.Probe.EventsBeforeCommit);
        var raw = Assert.Single(host.Probe.RawBeforeMatchingCommit);
        Assert.Equal("unmatched", raw.MatchStatus);
        Assert.Null(raw.OrderId);
        // Người nhận phải đọc được tiền và lịch sử đã commit qua kết nối DB riêng.
        if (paid)
        {
            var observed = Assert.Single(host.Events.Observations);
            Assert.Equal(PaymentStatus.Paid, observed.PaymentStatus);
            Assert.Equal(OrderStatus.PendingConfirmation, observed.Status);
            Assert.Equal(1, observed.HistoryCount);
            Assert.Equal(1, observed.MatchedCount);
            Assert.Equal(order.Total + difference, observed.Event.Amount);
        }
        Assert.DoesNotContain(_factory.Logs.Entries, row => (row.Message + row.Exception).Contains(ApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MatchingCommitFailure_PreservesRawAndRollsBackOrderAndEvents()
    {
        var order = await CreateOrderAsync();
        var host = CreateHost();
        var payload = Payload(order.Code, order.Total);
        host.Probe.Arm(failMatchingCommit: true);
        await AssertSuccessAsync(await SendAsync(host.Client, payload));
        // Gửi lại qua host không gây lỗi vẫn không được khớp lại chứng từ đã nhận.
        var replayHost = CreateHost();
        await AssertSuccessAsync(await SendAsync(replayHost.Client, payload));
        Assert.Empty(replayHost.Events.Observations);
        var transaction = await ReadTransactionAsync(payload);
        using (transaction.Raw)
        {
            Assert.Equal("unmatched", transaction.MatchStatus);
            Assert.Null(transaction.OrderId);
        }
        var saved = await ReadOrderAsync(order.Id);
        Assert.Equal(OrderStatus.PendingPayment, saved.Status);
        Assert.Equal(PaymentStatus.Unpaid, saved.PaymentStatus);
        Assert.DoesNotContain(saved.StatusHistory, row => row.ToStatus == OrderStatus.PendingConfirmation);
        Assert.Empty(host.Events.Observations);
        Assert.Equal(1, host.Probe.MatchingCommitAttempts);
        Assert.Contains(_factory.Logs.Entries, row => (row.Exception ?? "").Contains(SepayWebhookTransactionProbe.FailureMessage, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("no code")]
    [InlineData("PK22222222")]
    public async Task MissingOrder_IsUnmatched(string content)
    {
        var host = CreateHost();
        var payload = Payload(content);
        await AssertSuccessAsync(await SendAsync(host.Client, payload));
        var transaction = await ReadTransactionAsync(payload);
        using (transaction.Raw)
        {
            Assert.Equal("unmatched", transaction.MatchStatus);
            Assert.Null(transaction.OrderId);
        }
        Assert.Empty(host.Events.Observations);
    }

    [Theory]
    [InlineData("cod", OrderStatus.PendingConfirmation, PaymentStatus.Unpaid)]
    [InlineData("sepay", OrderStatus.PendingConfirmation, PaymentStatus.Paid)]
    [InlineData("sepay", OrderStatus.Cancelled, PaymentStatus.Unpaid)]
    public async Task NonPendingOrder_IsUnmatchedWithoutMutation(string method, OrderStatus status, PaymentStatus paymentStatus)
    {
        var order = await CreateOrderAsync(method);
        await using (var commerce = _database.CreateCommerceContext())
        {
            // Đặt trạng thái đầu vào để kiểm luật webhook cho đơn COD, đã trả và đã hủy.
            await commerce.Orders.Where(row => row.Id == order.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, status).SetProperty(row => row.PaymentStatus, paymentStatus));
        }
        var host = CreateHost();
        var payload = Payload(order.Code, order.Total);
        await AssertSuccessAsync(await SendAsync(host.Client, payload));
        var transaction = await ReadTransactionAsync(payload);
        using (transaction.Raw)
        {
            Assert.Equal("unmatched", transaction.MatchStatus);
            Assert.Null(transaction.OrderId);
        }
        var saved = await ReadOrderAsync(order.Id);
        Assert.Equal(status, saved.Status);
        Assert.Equal(paymentStatus, saved.PaymentStatus);
        Assert.Empty(host.Events.Observations);
        Assert.Contains(_factory.Logs.Entries, row => row.Message.Contains(order.Code, StringComparison.Ordinal)
            && row.Message.Contains(status.ToString(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameTransactionThreeTimes_AcknowledgesEveryRequestButPaysOnce(bool concurrent)
    {
        var order = await CreateOrderAsync();
        var host = CreateHost();
        var payload = Payload(order.Code, order.Total);
        // Hàng rào buộc cả ba request đồng thời đi tới transaction lưu thô trước khi request nào chèn dòng.
        host.Probe.Arm(rawParticipants: concurrent ? 3 : 0);
        HttpResponseMessage[] responses;
        // Cùng host và cùng ID kiểm cả đường gửi tuần tự lẫn tranh ràng buộc unique của DB.
        if (concurrent)
        {
            responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => SendAsync(host.Client, payload)));
        }
        else
        {
            responses = [await SendAsync(host.Client, payload), await SendAsync(host.Client, payload), await SendAsync(host.Client, payload)];
        }
        foreach (var response in responses)
        {
            await AssertSuccessAsync(response);
        }
        await using var commerce = _database.CreateCommerceContext();
        var id = Convert.ToString(payload["id"], System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.Equal(1, await commerce.PaymentTransactions.CountAsync(row => row.ProviderTxId == id));
        Assert.Equal(1, await commerce.OrderStatusHistory.CountAsync(row => row.OrderId == order.Id && row.ToStatus == OrderStatus.PendingConfirmation));
        Assert.Single(host.Events.Observations);
        // Request trùng không được mở lại bước khớp, kể cả khi đơn đã trả tiền.
        Assert.Equal(1, host.Probe.MatchingCommitAttempts);
    }

    [Fact]
    public async Task SeparateUnderpayments_DoNotAccumulateOrDuplicateFlag()
    {
        var order = await CreateOrderAsync();
        var host = CreateHost();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await AssertSuccessAsync(await SendAsync(host.Client, Payload(order.Code, order.Total - 1)));
        }
        var saved = await ReadOrderAsync(order.Id);
        Assert.Equal(OrderStatus.PendingPayment, saved.Status);
        Assert.Equal(PaymentStatus.Unpaid, saved.PaymentStatus);
        Assert.Equal(new[] { "underpaid" }, saved.Flags);
        Assert.Empty(host.Events.Observations);
    }

    [Fact]
    public async Task DifferentTransactionsOnSameOrder_RowLockAllowsOnlyOnePaidEvent()
    {
        var order = await CreateOrderAsync();
        var host = CreateHost();
        var first = Payload(order.Code, order.Total);
        var second = Payload(order.Code, order.Total);
        // Hàng rào mở hai transaction khớp cùng lúc, không dùng ngủ để hy vọng xảy ra tranh khóa.
        host.Probe.Arm(matchingParticipants: 2);
        var responses = await Task.WhenAll(SendAsync(host.Client, first), SendAsync(host.Client, second));
        foreach (var response in responses)
        {
            await AssertSuccessAsync(response);
        }
        await using var commerce = _database.CreateCommerceContext();
        Assert.Equal(1, await commerce.PaymentTransactions.CountAsync(row => row.OrderId == order.Id && row.MatchStatus == "matched"));
        Assert.Equal(1, await commerce.PaymentTransactions.CountAsync(row => _providerIds.Contains(row.ProviderTxId) && row.OrderId == null && row.MatchStatus == "unmatched"));
        Assert.Equal(1, await commerce.OrderStatusHistory.CountAsync(row => row.OrderId == order.Id && row.ToStatus == OrderStatus.PendingConfirmation));
        Assert.Single(host.Events.Observations);
    }

    [Fact]
    public async Task RateLimit_CountsUnauthorizedRequestsBeforeAuthentication()
    {
        var host = CreateHost();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(host.Client, "{", authorization: null)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAsync(host.Client, Payload())).StatusCode);
        await using var commerce = _database.CreateCommerceContext();
        Assert.False(await commerce.PaymentTransactions.AnyAsync());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("gateway")]
    [InlineData("transactionDate")]
    [InlineData("accountNumber")]
    [InlineData("subAccount")]
    [InlineData("code")]
    [InlineData("content")]
    [InlineData("transferType")]
    [InlineData("description")]
    [InlineData("transferAmount")]
    [InlineData("accumulated")]
    [InlineData("referenceCode")]
    public async Task MissingRequiredField_RejectsWithoutWriting(string field)
    {
        var host = CreateHost();
        var payload = Payload();
        // Trường rỗng hợp lệ khác trường bị thiếu, nên bỏ hẳn thuộc tính để kiểm hợp đồng.
        payload.Remove(field);
        await CartTestKit.AssertErrorAsync(await SendAsync(host.Client, payload), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await using var commerce = _database.CreateCommerceContext();
        Assert.False(await commerce.PaymentTransactions.AnyAsync());
    }

    [Theory]
    [InlineData(16384, 200)]
    [InlineData(16385, 413)]
    public async Task UnknownContentLength_EnforcesExactByteBoundary(int length, int expectedStatus)
    {
        var host = CreateHost();
        var payload = Payload();
        var json = JsonSerializer.Serialize(payload);
        // Phần đệm ASCII cho tổng byte chính xác, không phụ thuộc cách mã hóa tiếng Việt.
        payload["description"] = new string('x', length - Encoding.UTF8.GetByteCount(json));
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        Assert.Equal(length, bytes.Length);
        // `TestServer` giữ `Content-Length` null để kiểm đường đọc có giới hạn, không chỉ kiểm header.
        var context = await host.Server.SendAsync(request =>
        {
            request.Request.Method = "POST";
            request.Request.Path = "/api/webhooks/sepay";
            request.Request.Headers.Authorization = "Apikey " + ApiKey;
            request.Request.ContentType = "application/json";
            request.Request.Body = new MemoryStream(bytes);
        });
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        await using var commerce = _database.CreateCommerceContext();
        Assert.Equal(expectedStatus == 200 ? 1 : 0, await commerce.PaymentTransactions.CountAsync());
    }

}
