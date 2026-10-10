using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Modules.Shipping.Webhooks;
using Xunit;

// Cả Contracts và Entities có kiểu `Shipment`. Test này làm việc với kiểu của bên gọi.
using Shipment = Pickory.Modules.Shipping.Contracts.Shipments.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-08 lát 3: `POST /api/webhooks/ghn` qua HTTP với DB thật và client GHN giả.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class GhnWebhookTests : IClassFixture<ConfiguredGhnWebApplicationFactory>, IAsyncLifetime
{
    private const string WebhookPath = "/api/webhooks/ghn";
    private const string SecretHeader = "X-Pickory-Webhook-Secret";
    private const string Secret = ConfiguredGhnWebApplicationFactory.FakeGhnWebhookSecret;

    private readonly ConfiguredGhnWebApplicationFactory _baseFactory;
    private readonly CapturingLoggerProvider _logs = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public GhnWebhookTests(ConfiguredGhnWebApplicationFactory factory)
    {
        _baseFactory = factory;
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(_logs)));
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    private FakeGhnClient Fake => _factory.Services.GetRequiredService<FakeGhnClient>();

    private IReadOnlyList<ShipmentStatusChangedEvent> StatusEvents() =>
        _factory.Services.GetRequiredService<RecordedDomainEvents>().OfType<ShipmentStatusChangedEvent>();

    public async Task InitializeAsync()
    {
        await ShipmentTestSetup.ResetAsync(_factory);
        await QuoteTestSetup.SeedCatalogAsync(_factory);
        await QuoteTestSetup.SeedSettingsAsync(_factory, defaultServiceTypeId: 2);
        _factory.Services.GetRequiredService<GhnWebhookRateLimiter>().Clear();
        Fake.ClearFailures();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await ShipmentTestSetup.ResetAsync(_factory);
        _factory.Dispose();
    }

    private static async Task<Shipment> CreateShipmentAsync(IServiceProvider services, string reference = "ship08-ref-1")
    {
        await using var scope = services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<IShippingService>();
        return await shipping.CreateShipmentAsync(ShipmentTestSetup.CreateRequest(reference), CancellationToken.None);
    }

    private Task<Shipment> CreateShipmentAsync(string reference = "ship08-ref-1") =>
        CreateShipmentAsync(_factory.Services, reference);

    private static HttpRequestMessage BuildRequest(string body, string? secret, bool withOrigin = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (secret is not null)
        {
            request.Headers.Add(SecretHeader, secret);
        }

        if (withOrigin)
        {
            request.Headers.Add("Origin", "http://localhost:5173");
        }

        return request;
    }

    // Payload GHN thật có nhiều trường PascalCase. `Status` ở đây cố ý có thể sai để test chứng minh module bỏ qua nó.
    private static string Payload(string orderCode, string payloadStatus = "ready_to_pick") =>
        JsonSerializer.Serialize(new { ShopID = 123456, OrderCode = orderCode, Status = payloadStatus, Type = "switch_status" });

    private Task<HttpResponseMessage> PostAsync(string orderCode, string? secret = Secret, string payloadStatus = "ready_to_pick") =>
        _client.SendAsync(BuildRequest(Payload(orderCode, payloadStatus), secret));

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    [Theory]
    [InlineData(null)]
    [InlineData("sai-secret")]
    [InlineData("")]
    public async Task Webhook_MissingOrWrongSecret_Returns401AndDoesNotTouchDbOrGhn(string? secret)
    {
        var shipment = await CreateShipmentAsync();
        var eventsBefore = StatusEvents().Count;

        var response = await PostAsync(shipment.TrackingCode, secret, payloadStatus: "delivered");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, Fake.OrderDetailCallCount);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(_factory));
        Assert.Equal(ShipmentStatus.PendingPickup, stored.Status);
        Assert.Single(await ShipmentTestSetup.ReadEventsAsync(_factory));
        Assert.Equal(eventsBefore, StatusEvents().Count);
    }

    [Fact]
    // Sai secret thì không đọc body. Body hỏng vẫn nhận 401 chứ không phải 400.
    public async Task Webhook_WrongSecretWithBrokenBody_Returns401()
    {
        var response = await _client.SendAsync(BuildRequest("{khong-phai-json", "sai-secret"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    // GHN gọi từ server nên không có `Origin` hay `Referer`. Route khác vẫn bị chặn khi thiếu `Origin`.
    public async Task Webhook_WithoutOriginAndReferer_IsNotBlockedButOtherWriteRouteIs()
    {
        var response = await PostAsync("KHONG-CO-TRONG-DB");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Route webhook không cấp cookie ẩn danh và không tạo actor khách.
        Assert.Empty(ActorHttp.ReadSetCookies(response, "pk_anon"));

        var otherRoute = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Post, "/api/health"));

        Assert.Equal(HttpStatusCode.Forbidden, otherRoute.StatusCode);
        using var document = await ReadJsonAsync(otherRoute);
        Assert.Equal("FORBIDDEN_ORIGIN", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    // Chỉ tiền tố `/api/webhooks/` được miễn. Đường dẫn khác có chữ `webhooks` vẫn bị kiểm `Origin`.
    public async Task Origin_PathThatOnlyLooksLikeWebhookPrefix_StillReturns403()
    {
        var response = await _client.SendAsync(
            ActorHttp.CreateRequest(HttpMethod.Post, "/api/webhooks-not-really/ghn"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("{khong-phai-json")]
    [InlineData("{}")]
    [InlineData("{\"OrderCode\":\"\"}")]
    [InlineData("{\"OrderCode\":123}")]
    [InlineData("[]")]
    public async Task Webhook_BrokenBodyOrMissingOrderCode_Returns400ValidationError(string body)
    {
        var response = await _client.SendAsync(BuildRequest(body, Secret));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await ReadJsonAsync(response);
        Assert.Equal("VALIDATION_ERROR", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, Fake.OrderDetailCallCount);
    }

    [Fact]
    public async Task Webhook_UnknownOrderCode_Returns200WithoutCallingGhnOrChangingDb()
    {
        await CreateShipmentAsync();

        var response = await PostAsync("MA-KHONG-CO-TRONG-DB");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, Fake.OrderDetailCallCount);
        Assert.Single(await ShipmentTestSetup.ReadEventsAsync(_factory));
    }

    [Fact]
    // Payload ghi `delivered` nhưng GHN chi tiết trả `picking`. DB theo API chi tiết, không theo payload.
    public async Task Webhook_PayloadStatusIsIgnored_UsesOrderDetailFromGhn()
    {
        var shipment = await CreateShipmentAsync();
        Fake.SetOrderStatus(shipment.TrackingCode, "picking");

        var response = await PostAsync(shipment.TrackingCode, payloadStatus: "delivered");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(_factory));
        Assert.Equal("picking", stored.RawStatus);
        Assert.Equal(ShipmentStatus.PendingPickup, stored.Status);
        Assert.Equal(1, Fake.OrderDetailCallCount);
    }

    [Fact]
    public async Task Webhook_StatusChanges_UpdatesShipmentAndPublishesOneEvent()
    {
        var shipment = await CreateShipmentAsync();
        var eventsBefore = StatusEvents().Count;
        Fake.SetOrderStatus(shipment.TrackingCode, "delivering");

        var response = await PostAsync(shipment.TrackingCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(_factory));
        Assert.Equal(ShipmentStatus.Delivering, stored.Status);
        var published = Assert.Single(StatusEvents().Skip(eventsBefore));
        Assert.Equal(shipment.Id, published.ShipmentId);
        Assert.Equal("ship08-ref-1", published.Ref);
        Assert.Equal(ShipmentStatus.PendingPickup, published.PreviousStatus);
        var history = await ShipmentTestSetup.ReadEventsAsync(_factory);
        Assert.Single(history, row => row.Status == ShipmentStatus.Delivering
            && row.Source == Pickory.Modules.Shipping.Entities.ShipmentEventSource.Webhook);
    }

    [Fact]
    public async Task Webhook_SameWebhookTwiceInSequence_PublishesOneEventAndWritesOneHistoryRow()
    {
        var shipment = await CreateShipmentAsync();
        var eventsBefore = StatusEvents().Count;
        Fake.SetOrderStatus(shipment.TrackingCode, "picked");

        var first = await PostAsync(shipment.TrackingCode);
        var second = await PostAsync(shipment.TrackingCode);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(StatusEvents().Skip(eventsBefore));
        Assert.Equal(2, (await ShipmentTestSetup.ReadEventsAsync(_factory)).Count);
    }

    [Fact]
    // Hai webhook cùng đọc trạng thái cũ vì cả hai bị giữ ở lời gọi chi tiết cho tới khi cả hai cùng tới.
    // Sau đó chỉ một bên ghi được nhờ điều kiện `RawStatus` khi ghi.
    public async Task Webhook_TwoConcurrentWebhooksForSameOrder_PublishOneEventAndWriteOneHistoryRow()
    {
        await using var gatedFactory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGhnClient>();
            services.AddSingleton<IGhnClient>(provider =>
                new GatedOrderDetailGhnClient(provider.GetRequiredService<FakeGhnClient>(), participants: 2));
        }));
        gatedFactory.Services.GetRequiredService<GhnWebhookRateLimiter>().Clear();
        var shipment = await CreateShipmentAsync(gatedFactory.Services);
        var gatedFake = gatedFactory.Services.GetRequiredService<FakeGhnClient>();
        gatedFake.SetOrderStatus(shipment.TrackingCode, "picked");
        var gatedEvents = gatedFactory.Services.GetRequiredService<RecordedDomainEvents>();
        var eventsBefore = gatedEvents.OfType<ShipmentStatusChangedEvent>().Count;
        using var gatedClient = gatedFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var responses = await Task.WhenAll(
            gatedClient.SendAsync(BuildRequest(Payload(shipment.TrackingCode), Secret)),
            gatedClient.SendAsync(BuildRequest(Payload(shipment.TrackingCode), Secret)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(2, gatedFake.OrderDetailCallCount);
        Assert.Single(gatedEvents.OfType<ShipmentStatusChangedEvent>().Skip(eventsBefore));
        Assert.Equal(2, (await ShipmentTestSetup.ReadEventsAsync(gatedFactory)).Count);
    }

    [Fact]
    public async Task Webhook_RawStatusChangesButStatusDoesNot_WritesHistoryRowWithoutEvent()
    {
        var shipment = await CreateShipmentAsync();
        var eventsBefore = StatusEvents().Count;
        Fake.SetOrderStatus(shipment.TrackingCode, "picking");

        var response = await PostAsync(shipment.TrackingCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(eventsBefore, StatusEvents().Count);
        var history = await ShipmentTestSetup.ReadEventsAsync(_factory);
        Assert.Equal(2, history.Count);
        Assert.Single(history, row => row.RawStatus == "picking");
    }

    [Fact]
    public async Task Webhook_UnknownGhnStatus_LogsWarningKeepsStatusAndReturns200()
    {
        var shipment = await CreateShipmentAsync();
        Fake.SetOrderStatus(shipment.TrackingCode, "trang_thai_moi_cua_ghn");

        var response = await PostAsync(shipment.TrackingCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(_factory));
        Assert.Equal("ready_to_pick", stored.RawStatus);
        Assert.Equal(ShipmentStatus.PendingPickup, stored.Status);
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("trang_thai_moi_cua_ghn", StringComparison.Ordinal));
        Assert.Contains(shipment.TrackingCode, warning.Message);
    }

    [Fact]
    public async Task Webhook_GhnNetworkErrorOnDetail_Returns503SoGhnRetries()
    {
        var shipment = await CreateShipmentAsync();
        Fake.SetFailure(FakeGhnOperation.GetOrderDetail, FakeGhnFailure.Unavailable);

        var response = await PostAsync(shipment.TrackingCode);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, Fake.OrderDetailCallCount);
        Assert.Single(await ShipmentTestSetup.ReadEventsAsync(_factory));
    }

    [Fact]
    // GHN từ chối thì thử lại không giúp được. Trả 200 để GHN không gửi lại, và ghi cảnh báo kèm mã vận đơn.
    public async Task Webhook_GhnRejectsDetail_Returns200AndLogsWarning()
    {
        var shipment = await CreateShipmentAsync();
        Fake.SetFailure(FakeGhnOperation.GetOrderDetail, FakeGhnFailure.Rejected);

        var response = await PostAsync(shipment.TrackingCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, Fake.OrderDetailCallCount);
        Assert.Contains(_logs.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains(shipment.TrackingCode, StringComparison.Ordinal));
    }

    [Fact]
    // Request thứ 301 trong một phút từ cùng IP nhận 429. Request sai secret cũng được tính, để chậm việc dò secret.
    public async Task Webhook_MoreThan300RequestsPerMinute_Returns429()
    {
        for (var attempt = 1; attempt <= GhnWebhookRateLimiter.MaxRequestsPerWindow; attempt++)
        {
            var response = await PostAsync("MA-KHONG-CO", secret: "sai-secret");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await PostAsync("MA-KHONG-CO", secret: Secret);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var document = await ReadJsonAsync(limited);
        Assert.Equal("TOO_MANY_REQUESTS", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Webhook_GhnReportsCancel_CancelsShipmentRenamesRefAndLaterCreateUsesSuffixedClientCode()
    {
        var shipment = await CreateShipmentAsync();
        var eventsBefore = StatusEvents().Count;
        Fake.SetOrderStatus(shipment.TrackingCode, "cancel");

        var response = await PostAsync(shipment.TrackingCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(_factory));
        Assert.Equal(ShipmentStatus.Cancelled, stored.Status);
        Assert.Equal("ship08-ref-1#cancelled#1", stored.Ref);
        var published = Assert.Single(StatusEvents().Skip(eventsBefore));
        Assert.Equal("ship08-ref-1", published.Ref);
        Assert.Equal(ShipmentStatus.Cancelled, published.Status);

        var recreated = await CreateShipmentAsync();

        Assert.NotEqual(shipment.Id, recreated.Id);
        Assert.Equal("ship08-ref-1", recreated.Ref);
        var detail = await Fake.GetOrderDetailAsync(recreated.TrackingCode, CancellationToken.None);
        Assert.Equal("ship08-ref-1-2", detail.ClientOrderCode);
    }

    [Fact]
    // Log của webhook không chứa secret, token, số điện thoại, tên hay địa chỉ người nhận.
    public async Task Webhook_Logs_DoNotContainSecretsOrRecipientData()
    {
        var shipment = await CreateShipmentAsync();
        Fake.SetOrderStatus(shipment.TrackingCode, "mot_trang_thai_la");
        await PostAsync(shipment.TrackingCode);
        await PostAsync(shipment.TrackingCode, secret: "sai-secret");
        Fake.SetFailure(FakeGhnOperation.GetOrderDetail, FakeGhnFailure.Rejected);
        await PostAsync(shipment.TrackingCode);

        var forbidden = new[]
        {
            ConfiguredGhnWebApplicationFactory.FakeGhnWebhookSecret,
            ConfiguredGhnWebApplicationFactory.FakeGhnToken,
            "sai-secret",
            "0987654321",
            "Nguyễn Văn Khách",
            "12 Phố Test",
        };
        foreach (var entry in _logs.Entries)
        {
            foreach (var value in forbidden)
            {
                Assert.DoesNotContain(value, entry.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(value, entry.Exception ?? string.Empty, StringComparison.Ordinal);
            }
        }
    }
}
