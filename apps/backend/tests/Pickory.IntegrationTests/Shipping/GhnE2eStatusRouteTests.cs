using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Modules.Shipping.Ghn;
using Pickory.Shared.Configuration;
using Xunit;

// Cả Contracts và Entities có kiểu `Shipment`. Test này làm việc với kiểu của bên gọi.
using Shipment = Pickory.Modules.Shipping.Contracts.Shipments.Shipment;

namespace Pickory.IntegrationTests.Shipping;

// COM-E2E3: route `POST /api/e2e/ghn/orders/{trackingCode}/status` đổi trạng thái đơn của `FakeGhnClient` cho Playwright.
// Route mở một cửa đổi trạng thái đơn không cần đăng nhập, nên các test dưới đây chứng minh route không có ở host thường.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class GhnE2eStatusRouteTests(IntegrationDatabaseFixture database) : IAsyncLifetime
{
    private const string WebhookSecret = "e2e-ghn-webhook-secret";

    private readonly List<IDisposable> _disposables = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        // Dọn vận đơn mà các test tạo, để test khác trong cùng DB không thấy dữ liệu thừa.
        using var cleanupFactory = new PickoryWebApplicationFactory(database);
        await ShipmentTestSetup.ResetAsync(cleanupFactory);
    }

    private static string StatusPath(string trackingCode) => $"/api/e2e/ghn/orders/{trackingCode}/status";

    // Host chạy như `dotnet run` thường: `Program.cs` không map route E2E.
    private WebApplicationFactory<Program> CreateRegularHostFactory()
    {
        var factory = new PickoryWebApplicationFactory(database);
        _disposables.Add(factory);
        return factory;
    }

    // Host mô phỏng `e2e:start` với client GHN giả, thiếu `GHN_TOKEN`, và có secret webhook như `playwright.config.ts`.
    private WebApplicationFactory<Program> CreateE2eFactory()
    {
        var factory = new PickoryWebApplicationFactory(database).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter, E2eShippingRoutesStartupFilter>();
                var currentConfig = (AppConfig)services.Single(descriptor => descriptor.ServiceType == typeof(AppConfig)).ImplementationInstance!;
                services.RemoveAll<AppConfig>();
                services.AddSingleton(currentConfig with { GhnWebhookSecret = WebhookSecret });
            }));
        _disposables.Add(factory);
        return factory;
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static HttpRequestMessage StatusRequest(string trackingCode, string body, bool withOrigin = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, StatusPath(trackingCode))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (withOrigin)
        {
            request.Headers.Add("Origin", "http://localhost:5173");
        }

        return request;
    }

    private static string StatusBody(string status) => JsonSerializer.Serialize(new { status });

    private static async Task<Shipment> CreateShipmentAsync(WebApplicationFactory<Program> factory)
    {
        await ShipmentTestSetup.ResetAsync(factory);
        await QuoteTestSetup.SeedCatalogAsync(factory);
        await QuoteTestSetup.SeedSettingsAsync(factory, defaultServiceTypeId: 2);
        await using var scope = factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<IShippingService>();
        return await shipping.CreateShipmentAsync(ShipmentTestSetup.CreateRequest("e2e-route-ref-1"), CancellationToken.None);
    }

    // Trạng thái trong client GHN giả còn nguyên như ban đầu, chứng minh request vừa rồi không đổi được nó.
    private static async Task AssertGhnStatusUnchangedAsync(WebApplicationFactory<Program> factory, string trackingCode)
    {
        var ghnClient = factory.Services.GetRequiredService<FakeGhnClient>();
        var detail = await ghnClient.GetOrderDetailAsync(trackingCode, CancellationToken.None);
        Assert.Equal("ready_to_pick", detail.Status);
    }

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }

    [Fact]
    // Host thường không chạy lệnh `e2e:start`, nên cửa đổi trạng thái đơn không được tồn tại.
    // Test dùng mã vận đơn có thật, vì mã lạ cũng trả 404 và không chứng minh được route vắng mặt.
    public async Task Route_RegularHost_Returns404AndLeavesGhnStatusUnchanged()
    {
        var factory = CreateRegularHostFactory();
        using var client = CreateClient(factory);
        var shipment = await CreateShipmentAsync(factory);

        var response = await client.SendAsync(StatusRequest(shipment.TrackingCode, StatusBody("delivered")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertGhnStatusUnchangedAsync(factory, shipment.TrackingCode);
    }

    [Fact]
    // Có `GHN_TOKEN` thì API dùng client GHN thật, nên route không được map dù DI của test vẫn giữ client giả.
    public async Task Route_HostWithGhnToken_Returns404AndLeavesGhnStatusUnchanged()
    {
        var factory = new ConfiguredGhnWebApplicationFactory(database).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter, E2eShippingRoutesStartupFilter>()));
        _disposables.Add(factory);
        using var client = CreateClient(factory);
        var shipment = await CreateShipmentAsync(factory);

        var response = await client.SendAsync(StatusRequest(shipment.TrackingCode, StatusBody("delivered")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertGhnStatusUnchangedAsync(factory, shipment.TrackingCode);
    }

    [Fact]
    // Chế độ E2E: đặt `delivered` rồi gọi webhook đúng secret thì vận đơn sang `Delivered`.
    public async Task Route_E2eMode_SetsStatusThenWebhookMovesShipmentToDelivered()
    {
        var factory = CreateE2eFactory();
        using var client = CreateClient(factory);
        var shipment = await CreateShipmentAsync(factory);

        var statusResponse = await client.SendAsync(StatusRequest(shipment.TrackingCode, StatusBody("delivered")));
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/ghn")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { OrderCode = shipment.TrackingCode }), Encoding.UTF8, "application/json"),
        };
        webhook.Headers.Add("X-Pickory-Webhook-Secret", WebhookSecret);
        var webhookResponse = await client.SendAsync(webhook);

        Assert.Equal(HttpStatusCode.OK, webhookResponse.StatusCode);
        var stored = Assert.Single(await ShipmentTestSetup.ReadShipmentsAsync(factory));
        Assert.Equal(ShipmentStatus.Delivered, stored.Status);
    }

    [Fact]
    // Mã vận đơn lạ trả 404 có mã lỗi rõ ràng, không phải 500.
    public async Task Route_E2eMode_UnknownTrackingCode_Returns404WithErrorCode()
    {
        var factory = CreateE2eFactory();
        using var client = CreateClient(factory);

        var response = await client.SendAsync(StatusRequest("MA-KHONG-TON-TAI", StatusBody("delivered")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SHIPMENT_NOT_FOUND", await ReadErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("trang-thai-la")]
    [InlineData("")]
    [InlineData("DELIVERED")]
    public async Task Route_E2eMode_UnknownStatus_Returns400ValidationError(string status)
    {
        var factory = CreateE2eFactory();
        using var client = CreateClient(factory);
        var shipment = await CreateShipmentAsync(factory);

        var response = await client.SendAsync(StatusRequest(shipment.TrackingCode, StatusBody(status)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", await ReadErrorCodeAsync(response));
    }

    [Theory]
    [InlineData("{khong-phai-json")]
    [InlineData("{}")]
    [InlineData("{\"status\":123}")]
    [InlineData("[]")]
    public async Task Route_E2eMode_BrokenBody_Returns400ValidationError(string body)
    {
        var factory = CreateE2eFactory();
        using var client = CreateClient(factory);
        var shipment = await CreateShipmentAsync(factory);

        var response = await client.SendAsync(StatusRequest(shipment.TrackingCode, body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", await ReadErrorCodeAsync(response));
    }

    [Fact]
    // Body lớn hơn 4 KB bị từ chối bằng lỗi validation, dù trạng thái trong body hợp lệ.
    public async Task Route_E2eMode_BodyOverSizeLimit_Returns400ValidationError()
    {
        var factory = CreateE2eFactory();
        using var client = CreateClient(factory);
        var shipment = await CreateShipmentAsync(factory);
        var oversizedBody = JsonSerializer.Serialize(new { status = "delivered", padding = new string('a', 5 * 1024) });

        var response = await client.SendAsync(StatusRequest(shipment.TrackingCode, oversizedBody));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", await ReadErrorCodeAsync(response));
        await AssertGhnStatusUnchangedAsync(factory, shipment.TrackingCode);
    }

    [Fact]
    // Route không nằm trong danh sách miễn `Origin` như webhook, nên request thiếu `Origin` bị actor middleware chặn.
    public async Task Route_E2eMode_WithoutOrigin_Returns403()
    {
        var factory = CreateE2eFactory();
        using var client = CreateClient(factory);

        var response = await client.SendAsync(StatusRequest("FAKE000001", StatusBody("delivered"), withOrigin: false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
