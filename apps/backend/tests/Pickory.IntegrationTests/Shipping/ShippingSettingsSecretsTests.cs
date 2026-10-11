using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Shipping.Persistence;
using Xunit;

namespace Pickory.IntegrationTests.Shipping;

// SHIP-06: response cấu hình shipping không lộ token, bí mật webhook của GHN. `ghnShopId` đọc từ biến môi trường.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class ShippingSettingsSecretsTests : IClassFixture<ConfiguredGhnWebApplicationFactory>, IAsyncLifetime
{
    private const string SettingsPath = "/api/admin/shipping/settings";
    private static readonly DateTime SyncedAt = new(2026, 10, 10, 6, 30, 15, DateTimeKind.Utc);

    private readonly ConfiguredGhnWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public ShippingSettingsSecretsTests(ConfiguredGhnWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync() => await ResetAsync();

    public async Task DisposeAsync() => await ResetAsync();

    private async Task ResetAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        // Test xóa cả bảng, nên phải chắc context trỏ tới DB test chứ không phải DB dev.
        DotnetSchemaIntegrationTests.AssertTestConnection(shipping);
        await shipping.Settings.ExecuteDeleteAsync();
        await shipping.AddressWards.ExecuteDeleteAsync();
        await shipping.AddressProvinces.ExecuteDeleteAsync();
        await using var identity = _database.CreateIdentityContext();
        await identity.AuditLogs.Where(log => log.TargetType == "shipping_settings").ExecuteDeleteAsync();
    }

    private async Task SeedCatalogAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var shipping = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        shipping.AddressProvinces.Add(AddressTestDatabase.Province("01", "Hà Nội", isActive: true, SyncedAt));
        await shipping.SaveChangesAsync();
        shipping.AddressWards.Add(AddressTestDatabase.Ward("0101", "01", "Phường Ba Đình", isActive: true, SyncedAt));
        await shipping.SaveChangesAsync();
    }

    private static object ValidBody() => new
    {
        senderName = "Kho Pickory",
        senderPhone = "0912345678",
        provinceCode = "01",
        wardCode = "0101",
        line = "12 Phố Test",
        defaultServiceTypeId = 2,
    };

    [Fact]
    // `GHN_SHOP_ID` hiện cho admin xem. Thân response thô của GET không chứa token hay bí mật webhook.
    public async Task Get_ReturnsShopIdAndNeverLeaksGhnSecrets()
    {
        await SeedCatalogAsync();
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "ship06");
        await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, ValidBody(), $"pk_session={adminSession}"));

        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, SettingsPath, $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(rawBody);
        Assert.Equal(ConfiguredGhnWebApplicationFactory.FakeGhnShopId, document.RootElement.GetProperty("data").GetProperty("ghnShopId").GetString());
        AssertNoGhnSecret(rawBody);
    }

    [Fact]
    // Thân response thô của PUT cũng không chứa token hay bí mật webhook.
    public async Task Put_ReturnsShopIdAndNeverLeaksGhnSecrets()
    {
        await SeedCatalogAsync();
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "ship06");

        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, ValidBody(), $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(rawBody);
        Assert.Equal(ConfiguredGhnWebApplicationFactory.FakeGhnShopId, document.RootElement.GetProperty("data").GetProperty("ghnShopId").GetString());
        AssertNoGhnSecret(rawBody);
    }

    [Fact]
    // PUT không nhận `ghnShopId`, vì giá trị này chỉ đến từ biến môi trường.
    public async Task Put_WithGhnShopIdInBody_Returns400()
    {
        await SeedCatalogAsync();
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "ship06");
        var body = new Dictionary<string, object?>
        {
            ["senderName"] = "Kho Pickory",
            ["senderPhone"] = "0912345678",
            ["provinceCode"] = "01",
            ["wardCode"] = "0101",
            ["line"] = "12 Phố Test",
            ["ghnShopId"] = "999",
        };

        var response = await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, body, $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static void AssertNoGhnSecret(string rawBody)
    {
        Assert.DoesNotContain(ConfiguredGhnWebApplicationFactory.FakeGhnToken, rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain(ConfiguredGhnWebApplicationFactory.FakeGhnWebhookSecret, rawBody, StringComparison.Ordinal);
    }
}
