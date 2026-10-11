using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Database;
using Pickory.IntegrationTests.Infrastructure;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-06: response và audit của cấu hình commerce không lộ `SEPAY_WEBHOOK_API_KEY`.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CommerceSettingsSecretsTests : IClassFixture<ConfiguredSepayWebApplicationFactory>, IAsyncLifetime
{
    private const string SettingsPath = "/api/admin/commerce/settings";
    private const string AuditTargetType = "commerce_settings";

    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public CommerceSettingsSecretsTests(ConfiguredSepayWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync() => await ResetAsync();

    public async Task DisposeAsync() => await ResetAsync();

    private async Task ResetAsync()
    {
        await using (var commerce = _database.CreateCommerceContext())
        {
            // Test xóa cả bảng, nên phải chắc context trỏ tới DB test chứ không phải DB dev.
            DotnetSchemaIntegrationTests.AssertTestConnection(commerce);
            await commerce.Settings.ExecuteDeleteAsync();
        }

        await using var identity = _database.CreateIdentityContext();
        await identity.AuditLogs.Where(log => log.TargetType == AuditTargetType).ExecuteDeleteAsync();
    }

    private static object ValidBody() => new
    {
        freeShippingThreshold = 600_000,
        codMaxAmount = 10_000_000,
        maxQuantityPerSku = 4,
        maxCartLines = 15,
        sepayAccountNumber = "0123456789",
        sepayBankCode = "MB",
    };

    [Fact]
    // Thân response thô của GET không chứa khóa, kể cả khi cấu hình đã lưu.
    public async Task Get_NeverContainsWebhookApiKey()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "com06");
        await _client.SendAsync(ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, ValidBody(), $"pk_session={adminSession}"));

        var response = await _client.SendAsync(ActorHttp.CreateRequest(HttpMethod.Get, SettingsPath, $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ConfiguredSepayWebApplicationFactory.FakeSepayWebhookApiKey, rawBody);
        Assert.DoesNotContain("webhook", rawBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    // Thân response thô của PUT và dòng audit không chứa khóa.
    public async Task Put_NeverLeaksWebhookApiKeyInResponseOrAudit()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "com06");

        var response = await _client.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, ValidBody(), $"pk_session={adminSession}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ConfiguredSepayWebApplicationFactory.FakeSepayWebhookApiKey, rawBody);
        await using var identity = _database.CreateIdentityContext();
        var audit = await identity.AuditLogs.AsNoTracking().SingleAsync(log => log.TargetType == AuditTargetType);
        var auditText = $"{audit.Before?.RootElement.GetRawText()}{audit.After?.RootElement.GetRawText()}{audit.Action}{audit.TargetId}";
        Assert.DoesNotContain(ConfiguredSepayWebApplicationFactory.FakeSepayWebhookApiKey, auditText);
        Assert.False(string.IsNullOrEmpty(auditText), "Audit phải có nội dung, nếu không phép kiểm trên không có ý nghĩa.");
    }
}
