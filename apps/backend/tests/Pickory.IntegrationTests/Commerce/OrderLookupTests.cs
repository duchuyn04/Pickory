using System.Net;
using System.Net.Http.Headers;
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
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Orders.Lookup;
using Pickory.Shared.Email;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-25: Tra cứu đơn hàng bằng mã đơn và email.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed partial class OrderLookupTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string LookupPath = "/api/order-lookups";
    private const string OrdersPath = "/api/orders";
    private const string PreviewPath = "/api/checkout/previews";
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];
    private readonly List<WebApplicationFactory<Program>> _derivedFactories = [];
    private int _logsBefore;

    public OrderLookupTests(CheckoutWebApplicationFactory factory, IntegrationDatabaseFixture database)
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
        _factory.Services.GetRequiredService<OrderLookupRateLimiter>().Clear();
        _factory.GhnClient.ClearFailures();
        // Ghi lại số lượng log trước khi test chạy để chỉ kiểm tra log mới.
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

    private string NewKey()
    {
        var key = "key-" + Guid.NewGuid().ToString("N");
        _keys.Add(key);
        return key;
    }

    private static string NewEmail() => $"khach-{Guid.NewGuid():N}@example.com";

    private async Task<(string Code, string Token, string ContactEmail)> CreateOrderAsync(string? contactEmail = null)
    {
        var email = contactEmail ?? NewEmail();
        var anonymousId = Guid.NewGuid().ToString("N");
        var product = await _kit.AddProductAsync(price: 100_000, stock: 10);
        var skuId = product.SkuIds[0];

        var addResponse = await _kit.AddAsync(anonymousId, skuId, 2);
        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);

        var preview = new
        {
            address = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            paymentMethod = "cod",
        };
        var previewResponse = await _kit.SendAsync(HttpMethod.Post, PreviewPath, anonymousId, preview);
        var data = await CartTestKit.ReadCartAsync(previewResponse);
        var total = data.GetProperty("total").GetInt32();

        var orderBody = new Dictionary<string, object?>
        {
            ["contact"] = new { name = "Nguyễn Văn An", phone = "0901234567", email },
            ["address"] = new { provinceCode = ProvinceCode, wardCode = WardCode, line = "12 Phố Test" },
            ["paymentMethod"] = "cod",
            ["expectedTotal"] = total,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, OrdersPath);
        request.Headers.Add("Cookie", $"pk_anon={anonymousId}");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Idempotency-Key", NewKey());
        request.Content = new StringContent(JsonSerializer.Serialize(orderBody), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        var orderData = json.GetProperty("data");
        var code = orderData.GetProperty("code").GetString()!;
        var token = orderData.GetProperty("accessToken").GetString()!;
        return (code, token, email);
    }

    private static HttpRequestMessage CreateLookupRequest(string orderCode, string email)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, LookupPath);
        request.Headers.Add("Origin", "http://localhost:5173");
        var body = JsonSerializer.Serialize(new { orderCode, email });
        request.Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        return request;
    }

    private static Task<HttpResponseMessage> SendLookupAsync(HttpClient client, string orderCode, string email)
    {
        return client.SendAsync(CreateLookupRequest(orderCode, email));
    }

    private async Task<Order?> FindOrderAsync(string code)
    {
        await using var commerce = _kit.Database.CreateCommerceContext();
        return await commerce.Orders.AsNoTracking().SingleOrDefaultAsync(order => order.Code == code);
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

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public const string FailureText = "Máy chủ thư không phản hồi.";
        public EmailMessage? AttemptedMessage { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            AttemptedMessage = message;
            throw new InvalidOperationException(FailureText);
        }
    }

    [Fact]
    // Khớp và không khớp trả cùng mã 202 và chuỗi body giống hệt nhau.
    public async Task OrderLookup_MatchAndNonMatch_ReturnSameStatusCode202AndExactSameBody()
    {
        var (code, token, contactEmail) = await CreateOrderAsync();

        var matchResponse = await SendLookupAsync(_client, code, contactEmail);
        var matchBody = await matchResponse.Content.ReadAsStringAsync();

        var nonExistentResponse = await SendLookupAsync(_client, "PK99999999", "nonexistent@example.com");
        var nonExistentBody = await nonExistentResponse.Content.ReadAsStringAsync();

        var wrongEmailResponse = await SendLookupAsync(_client, code, "wrong@example.com");
        var wrongEmailBody = await wrongEmailResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, matchResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, nonExistentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, wrongEmailResponse.StatusCode);

        Assert.Equal(matchBody, nonExistentBody);
        Assert.Equal(matchBody, wrongEmailBody);

        Assert.DoesNotContain(token, matchBody);
        Assert.DoesNotContain("?t=", matchBody);
        Assert.DoesNotContain(code, matchBody);

        using var doc = JsonDocument.Parse(matchBody);
        var message = doc.RootElement.GetProperty("data").GetProperty("message").GetString();
        Assert.Equal("Nếu thông tin đúng, link xem đơn đã được gửi tới email của đơn.", message);
    }

    [Fact]
    // Khớp thông tin thì gửi link mới mở được đơn, link cũ bị vô hiệu hóa trả 404.
    public async Task OrderLookup_Match_SendsEmailWithNewTokenAndInvalidatesOldToken()
    {
        var (code, oldToken, contactEmail) = await CreateOrderAsync();

        var response = await SendLookupAsync(_client, code, contactEmail);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var messages = _factory.EmailSender.MessagesTo(contactEmail)
            .Where(m => m.Metadata.TryGetValue("purpose", out var p) && p == "order-lookup")
            .ToList();
        var message = Assert.Single(messages);
        Assert.Equal("order-lookup", message.Metadata["purpose"]);
        Assert.Equal(code, message.Metadata["orderCode"]);

        var newToken = TokenInLink(message.Text);
        Assert.NotEqual(oldToken, newToken);

        // Mở đơn bằng token mới nhận 200 OK.
        var newLinkResponse = await _client.GetAsync($"/api/orders/{code}?t={newToken}");
        Assert.Equal(HttpStatusCode.OK, newLinkResponse.StatusCode);

        // Mở đơn bằng token cũ nhận 404 ORDER_NOT_FOUND.
        var oldLinkResponse = await _client.GetAsync($"/api/orders/{code}?t={oldToken}");
        Assert.Equal(HttpStatusCode.NotFound, oldLinkResponse.StatusCode);
        var oldJson = await CartTestKit.ReadJsonAsync(oldLinkResponse);
        Assert.Equal("ORDER_NOT_FOUND", oldJson.GetProperty("error").GetProperty("code").GetString());
        // Log của hệ thống không được chứa token mới.
        var lines = LogLinesOfThisTest().ToList();
        Assert.DoesNotContain(lines, line => line.Contains(newToken));
    }

    [Fact]
    // Email viết hoa và có khoảng trắng, mã đơn viết thường vẫn khớp thông tin.
    public async Task OrderLookup_CasingAndWhitespaceDifferences_StillMatch()
    {
        var email = NewEmail();
        var (code, _, _) = await CreateOrderAsync(email);

        // Lưu email liên hệ trong DB với chữ hoa và khoảng trắng thừa để kiểm tra phía DB.
        var uppercaseWithSpaces = $"  {email.ToUpperInvariant()}  ";
        await using (var commerce = _kit.Database.CreateCommerceContext())
        {
            var rawContact = JsonSerializer.Serialize(new
            {
                name = "Nguyễn Văn An",
                phone = "0901234567",
                email = uppercaseWithSpaces,
            });
            await commerce.Orders.Where(o => o.Code == code)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Contact, JsonDocument.Parse(rawContact, default)));
        }

        var lookupCode = code.ToLowerInvariant();
        var lookupEmail = $"  {email.ToLowerInvariant()}  ";

        var response = await SendLookupAsync(_client, lookupCode, lookupEmail);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Thư gửi tới email liên hệ đã lưu trên đơn.
        var messages = _factory.EmailSender.MessagesTo(uppercaseWithSpaces.Trim())
            .Where(m => m.Metadata.TryGetValue("purpose", out var p) && p == "order-lookup")
            .ToList();
        Assert.Single(messages);
    }

    [Fact]
    // Không khớp mã hoặc email thì không gửi email và không đổi token của đơn nào.
    public async Task OrderLookup_NonMatch_SendsNoEmailAndDoesNotChangeStoredTokenHash()
    {
        var (code, _, contactEmail) = await CreateOrderAsync();
        var orderBefore = await FindOrderAsync(code);
        var hashBefore = orderBefore!.AccessTokenHash;

        // Mã đơn không tồn tại.
        var nonExistentResponse = await SendLookupAsync(_client, "PK99999999", contactEmail);
        Assert.Equal(HttpStatusCode.Accepted, nonExistentResponse.StatusCode);

        // Email không khớp.
        var wrongEmailResponse = await SendLookupAsync(_client, code, "wrong@example.com");
        Assert.Equal(HttpStatusCode.Accepted, wrongEmailResponse.StatusCode);

        // Hộp thư không có thư tra cứu mới.
        var lookupMessages = _factory.EmailSender.MessagesTo(contactEmail)
            .Where(m => m.Metadata.TryGetValue("purpose", out var p) && p == "order-lookup")
            .ToList();
        Assert.Empty(lookupMessages);
        Assert.Empty(_factory.EmailSender.MessagesTo("wrong@example.com"));

        // Hash trong DB không đổi.
        var orderAfter = await FindOrderAsync(code);
        Assert.Equal(hashBefore, orderAfter!.AccessTokenHash);
    }

    [Fact]
    // Mã đơn hoặc email sai định dạng trả 400 VALIDATION_ERROR đúng tên trường.
    public async Task OrderLookup_InvalidFormat_Returns400ValidationErrorWithField()
    {
        var invalidCodeResponse = await SendLookupAsync(_client, "AB123", "an@example.com");
        Assert.Equal(HttpStatusCode.BadRequest, invalidCodeResponse.StatusCode);
        var invalidCodeJson = await CartTestKit.ReadJsonAsync(invalidCodeResponse);
        Assert.Equal("VALIDATION_ERROR", invalidCodeJson.GetProperty("error").GetProperty("code").GetString());
        var codeDetails = invalidCodeJson.GetProperty("error").GetProperty("details");
        Assert.Contains(codeDetails.EnumerateArray(), d => d.GetProperty("key").GetString() == "orderCode");

        var invalidEmailResponse = await SendLookupAsync(_client, "PK23456789", "invalid-email");
        Assert.Equal(HttpStatusCode.BadRequest, invalidEmailResponse.StatusCode);
        var invalidEmailJson = await CartTestKit.ReadJsonAsync(invalidEmailResponse);
        Assert.Equal("VALIDATION_ERROR", invalidEmailJson.GetProperty("error").GetProperty("code").GetString());
        var emailDetails = invalidEmailJson.GetProperty("error").GetProperty("details");
        Assert.Contains(emailDetails.EnumerateArray(), d => d.GetProperty("key").GetString() == "email");
    }

    [Fact]
    // Cùng IP với 5 email khác nhau thì lượt thứ 6 trả 429.
    public async Task OrderLookup_RateLimiting_SixthAttemptFromSameIpReturns429()
    {
        _factory.Services.GetRequiredService<OrderLookupRateLimiter>().Clear();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await SendLookupAsync(_client, "PK23456789", $"email{attempt}@example.com");
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var sixthResponse = await SendLookupAsync(_client, "PK23456789", "email6@example.com");
        Assert.Equal(HttpStatusCode.TooManyRequests, sixthResponse.StatusCode);
    }

    [Fact]
    // Cùng email từ 5 địa chỉ IP khác nhau qua proxy tin cậy thì lượt thứ 6 trả 429.
    public async Task OrderLookup_RateLimiting_SixthAttemptForSameEmailFromDifferentIpsReturns429()
    {
        _factory.Services.GetRequiredService<OrderLookupRateLimiter>().Clear();

        using var proxiedFactory = _factory.WithWebHostBuilder(builder => builder.UseSetting("TRUST_PROXY", "1"));
        using var proxiedClient = proxiedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var targetEmail = NewEmail();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var request = CreateLookupRequest("PK23456789", targetEmail);
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{attempt}");
            var response = await proxiedClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var sixthRequest = CreateLookupRequest("PK23456789", targetEmail);
        sixthRequest.Headers.Add("X-Forwarded-For", "198.51.100.6");
        var sixthResponse = await proxiedClient.SendAsync(sixthRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, sixthResponse.StatusCode);
    }
    [Fact]
    // Request lỗi định dạng 400 không bị tính vào lượt giới hạn của IP.
    public async Task OrderLookup_RateLimiting_ValidationErrorDoesNotConsumeQuota()
    {
        _factory.Services.GetRequiredService<OrderLookupRateLimiter>().Clear();

        // Gửi 5 request sai định dạng liên tiếp, đều nhận 400.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var invalidResponse = await SendLookupAsync(_client, "invalid-code", "not-an-email");
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        }

        // Sau đó gửi 5 request hợp lệ với 5 email khác nhau từ cùng một IP.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var validResponse = await SendLookupAsync(_client, "PK23456789", $"email{attempt}@example.com");
            Assert.Equal(HttpStatusCode.Accepted, validResponse.StatusCode);
        }
    }

    [Fact]
    // Khóa giới hạn theo email phải lấy từ body đã kiểm tra thay vì dùng một khóa cố định.
    public async Task OrderLookup_RateLimiting_DifferentEmailsFromDifferentIpsDoNotBlockEachOther()
    {
        _factory.Services.GetRequiredService<OrderLookupRateLimiter>().Clear();

        using var proxiedFactory = _factory.WithWebHostBuilder(builder => builder.UseSetting("TRUST_PROXY", "1"));
        using var proxiedClient = proxiedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Gửi 6 request với IP khác nhau và email khác nhau.
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var request = CreateLookupRequest("PK23456789", $"user{attempt}@example.com");
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{attempt}");
            var response = await proxiedClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
    }

    [Fact]
    // Bộ gửi email ném lỗi thì route vẫn trả 202 và log ghi mã đơn nhưng không chứa token hay link hay email.
    public async Task OrderLookup_EmailSenderThrows_Returns202AndLogsWithoutToken()
    {
        var (code, oldToken, contactEmail) = await CreateOrderAsync();

        ThrowingEmailSender? throwingSender = null;
        var failingClient = CreateHostWith(services =>
        {
            services.RemoveAll<IEmailSender>();
            throwingSender = new ThrowingEmailSender();
            services.AddSingleton<IEmailSender>(throwingSender);
        });

        var response = await SendLookupAsync(failingClient, code, contactEmail);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(throwingSender?.AttemptedMessage);
        var token = TokenInLink(throwingSender!.AttemptedMessage!.Text);

        // Lấy token mới từ DB qua hash.
        var order = await FindOrderAsync(code);
        Assert.NotNull(order);
        Assert.NotEqual(OrderAccessToken.Hash(oldToken), order!.AccessTokenHash);

        var errors = _factory.Logs.Entries.Skip(_logsBefore)
            .Where(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error && entry.Message.Contains(code))
            .ToList();
        var error = Assert.Single(errors);
        Assert.Contains(ThrowingEmailSender.FailureText, error.Exception);

        var lines = LogLinesOfThisTest().ToList();
        Assert.DoesNotContain(lines, line => line.Contains(token));
        Assert.DoesNotContain(lines, line => line.Contains("?t="));
        Assert.DoesNotContain(lines, line => line.Contains(contactEmail));
    }
}
