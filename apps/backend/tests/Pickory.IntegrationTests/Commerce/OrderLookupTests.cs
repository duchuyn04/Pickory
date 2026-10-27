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
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Orders.Lookup;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-25: Tra cứu đơn hàng bằng mã đơn và email, trả token để mở đơn ngay trên trình duyệt.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class OrderLookupTests : IClassFixture<CheckoutWebApplicationFactory>, IAsyncLifetime
{
    private const string LookupPath = "/api/order-lookups";
    private const string OrdersPath = "/api/orders";
    private const string PreviewPath = "/api/checkout/previews";
    private const string ProvinceCode = "1000001";
    private const string WardCode = "1003646";
    private const string NotFoundMessage = "Không tìm thấy đơn hàng với mã và email này.";

    private readonly CheckoutWebApplicationFactory _factory;
    private readonly CartTestKit _kit;
    private readonly HttpClient _client;
    private readonly List<string> _keys = [];
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

    private IEnumerable<string> LogLinesOfThisTest() =>
        _factory.Logs.Entries.Skip(_logsBefore).SelectMany(entry => new[] { entry.Message, entry.Exception ?? string.Empty });

    private int LookupEmailCount(string email) =>
        _factory.EmailSender.MessagesTo(email)
            .Count(message => message.Metadata.TryGetValue("purpose", out var purpose) && purpose == "order-lookup");

    [Fact]
    // Khớp thông tin trả mã đơn và token mới, không gửi thư tra cứu, token cũ hết hiệu lực.
    public async Task OrderLookup_Match_ReturnsCodeAndTokenWithoutSendingEmail()
    {
        var (code, oldToken, contactEmail) = await CreateOrderAsync();

        var response = await SendLookupAsync(_client, code, contactEmail);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        var data = json.GetProperty("data");
        Assert.Equal(code, data.GetProperty("code").GetString());
        var newToken = data.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newToken));
        Assert.NotEqual(oldToken, newToken);

        // Không còn thư tra cứu. Thư xác nhận đơn đã gửi lúc tạo đơn không thuộc luồng này.
        Assert.Equal(0, LookupEmailCount(contactEmail));

        // Token mới mở được đơn, token cũ nhận 404.
        var newLinkResponse = await _client.GetAsync($"/api/orders/{code}?t={Uri.EscapeDataString(newToken!)}");
        Assert.Equal(HttpStatusCode.OK, newLinkResponse.StatusCode);

        var oldLinkResponse = await _client.GetAsync($"/api/orders/{code}?t={Uri.EscapeDataString(oldToken)}");
        Assert.Equal(HttpStatusCode.NotFound, oldLinkResponse.StatusCode);

        // Token chỉ có trong response, không được ghi vào log.
        var lines = LogLinesOfThisTest().ToList();
        Assert.DoesNotContain(lines, line => line.Contains(newToken!));
        Assert.DoesNotContain(lines, line => line.Contains("?t="));
    }

    [Fact]
    // Không khớp mã hoặc email trả cùng một lỗi 404, không đổi token và không gửi thư.
    public async Task OrderLookup_NonMatch_Returns404AndDoesNotChangeToken()
    {
        var (code, _, contactEmail) = await CreateOrderAsync();
        var orderBefore = await FindOrderAsync(code);
        var hashBefore = orderBefore!.AccessTokenHash;

        // Mã đơn không tồn tại.
        var nonExistentResponse = await SendLookupAsync(_client, "PK99999999", contactEmail);
        Assert.Equal(HttpStatusCode.NotFound, nonExistentResponse.StatusCode);

        // Email không khớp.
        var wrongEmailResponse = await SendLookupAsync(_client, code, "wrong@example.com");
        Assert.Equal(HttpStatusCode.NotFound, wrongEmailResponse.StatusCode);

        var nonExistentJson = await CartTestKit.ReadJsonAsync(nonExistentResponse);
        var wrongEmailJson = await CartTestKit.ReadJsonAsync(wrongEmailResponse);
        Assert.Equal("ORDER_NOT_FOUND", nonExistentJson.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(NotFoundMessage, nonExistentJson.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(nonExistentJson.GetRawText(), wrongEmailJson.GetRawText());

        var orderAfter = await FindOrderAsync(code);
        Assert.Equal(hashBefore, orderAfter!.AccessTokenHash);
        Assert.Equal(0, LookupEmailCount(contactEmail));
        Assert.Equal(0, LookupEmailCount("wrong@example.com"));
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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await CartTestKit.ReadJsonAsync(response);
        var token = json.GetProperty("data").GetProperty("accessToken").GetString();
        var detailResponse = await _client.GetAsync($"/api/orders/{code}?t={Uri.EscapeDataString(token!)}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
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
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
            Assert.Equal(HttpStatusCode.NotFound, validResponse.StatusCode);
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
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
