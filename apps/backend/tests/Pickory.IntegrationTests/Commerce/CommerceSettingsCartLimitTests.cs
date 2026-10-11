using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-06: giỏ hàng đọc giới hạn mới ngay ở request sau khi PUT cấu hình thành công.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CommerceSettingsCartLimitTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const string SettingsPath = "/api/admin/commerce/settings";
    private const string AuditTargetType = "commerce_settings";

    private readonly CartTestKit _kit;
    private readonly IntegrationDatabaseFixture _database;
    private readonly HttpClient _client;

    public CommerceSettingsCartLimitTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _kit = new CartTestKit(factory, database);
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _kit.InitializeAsync();

    public async Task DisposeAsync()
    {
        await _kit.CleanupAsync();
        await using var identity = _database.CreateIdentityContext();
        await identity.AuditLogs.Where(log => log.TargetType == AuditTargetType).ExecuteDeleteAsync();
    }

    private static object SettingsBody(int maxQuantityPerSku) => new
    {
        freeShippingThreshold = 500_000,
        codMaxAmount = 20_000_000,
        maxQuantityPerSku,
        maxCartLines = 20,
        sepayAccountNumber = (string?)null,
        sepayBankCode = (string?)null,
    };

    [Fact]
    // PUT `maxQuantityPerSku` bằng 2 thì thêm 3 sản phẩm nhận 422. Nâng lên 5 thì cùng thao tác thành công ngay.
    public async Task Put_MaxQuantityPerSku_TakesEffectOnNextCartRequest()
    {
        var (_, adminSession) = await IdentityTestData.CreateStaffSessionAsync(_database, "admin", "com06");
        var product = await _kit.AddProductAsync(stock: 20);
        var skuId = product.SkuIds[0];
        var anonymousId = _kit.NewAnonymousId();

        var lowered = await _client.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, SettingsBody(2), $"pk_session={adminSession}"));
        Assert.Equal(HttpStatusCode.OK, lowered.StatusCode);
        var rejected = await _kit.AddAsync(anonymousId, skuId, quantity: 3);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var rejectedJson = await CartTestKit.ReadJsonAsync(rejected);
        Assert.Equal("CART_LIMIT_EXCEEDED", rejectedJson.GetProperty("error").GetProperty("code").GetString());

        var raised = await _client.SendAsync(
            ActorHttp.CreateJsonRequest(HttpMethod.Put, SettingsPath, SettingsBody(5), $"pk_session={adminSession}"));
        Assert.Equal(HttpStatusCode.OK, raised.StatusCode);
        var accepted = await _kit.AddAsync(anonymousId, skuId, quantity: 3);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }
}
