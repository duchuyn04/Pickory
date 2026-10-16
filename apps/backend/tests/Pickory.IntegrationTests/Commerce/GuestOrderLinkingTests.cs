using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Throttling;
using Pickory.Modules.Identity.Tokens;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-21: khi email của khách được xác thực, đơn vãng lai cùng email gắn vào tài khoản đó.
// Test phát sự kiện qua dispatcher thật, nên cả việc đăng ký handler trong module cũng được kiểm.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class GuestOrderLinkingTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly LinkingTestSeed _seed;
    private readonly HttpClient _client;

    public GuestOrderLinkingTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new CartTestKit(factory, database);
        _seed = new LinkingTestSeed(database);
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // Bộ giới hạn là singleton dùng chung. Xóa bộ đếm để lượt gọi của test trước không chặn test này.
        factory.Services.GetRequiredService<IdentityRateLimiters>().ClearAll();
    }

    public async Task InitializeAsync()
    {
        await _kit.InitializeAsync();
        // Đăng ký tài khoản mới cần role `customer` có sẵn trong DB.
        await IdentityTestData.SeedRolesAsync(_database);
    }

    public async Task DisposeAsync()
    {
        await _seed.CleanupAsync();
        await _kit.CleanupAsync();
    }

    private Task VerifyAsync(string userId, string email) =>
        _factory.Services.GetRequiredService<IEventDispatcher>().PublishAsync(new EmailVerifiedEvent(userId, email));

    private static string NewToken() => SecureTokens.GenerateToken();

    [Fact]
    // Hai đơn vãng lai cùng email, một đơn khác hoa thường và có dấu cách hai đầu, đều gắn vào tài khoản.
    public async Task EmailVerified_LinksGuestOrdersWithSameEmailIgnoringCaseAndSpaces()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var email = IdentityTestData.NewEmail("link");
        var exact = await _seed.AddOrderAsync(email, null, NewToken());
        var messy = await _seed.AddOrderAsync($"  {email.ToUpperInvariant()}  ", null, NewToken());

        await VerifyAsync(userId, email);

        Assert.Equal(userId, (await _seed.ReadOrderAsync(exact)).UserId);
        Assert.Equal(userId, (await _seed.ReadOrderAsync(messy)).UserId);
    }

    [Fact]
    // Email của sự kiện có khoảng trắng và chữ hoa vẫn khớp, vì cả hai phía được chuẩn hóa như nhau.
    public async Task EmailVerified_NormalizesTheEventEmailToo()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var email = IdentityTestData.NewEmail("link-event");
        var orderId = await _seed.AddOrderAsync(email, null, NewToken());

        await VerifyAsync(userId, $" {email.ToUpperInvariant()} ");

        Assert.Equal(userId, (await _seed.ReadOrderAsync(orderId)).UserId);
    }

    [Fact]
    // Đơn vãng lai email khác không đổi.
    public async Task EmailVerified_DoesNotTouchOrdersOfOtherEmails()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var email = IdentityTestData.NewEmail("link-mine");
        var other = await _seed.AddOrderAsync(IdentityTestData.NewEmail("link-other"), null, NewToken());

        await VerifyAsync(userId, email);

        Assert.Null((await _seed.ReadOrderAsync(other)).UserId);
    }

    [Fact]
    // Đơn đã gắn tài khoản khác, dù cùng email, không đổi chủ. Điều kiện `"userId" IS NULL` bảo vệ đơn đó.
    public async Task EmailVerified_DoesNotStealOrderOfAnotherAccount()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var (otherUserId, _) = await _kit.CreateCustomerAsync();
        var email = IdentityTestData.NewEmail("link-owned");
        var owned = await _seed.AddOrderAsync(email, otherUserId, NewToken());

        await VerifyAsync(userId, email);

        Assert.Equal(otherUserId, (await _seed.ReadOrderAsync(owned)).UserId);
    }

    [Fact]
    // Sau khi gắn, danh sách đơn có đơn đó và link token cũ vẫn mở được. Token băm không đổi, và không có sự kiện trạng thái.
    public async Task EmailVerified_OrderAppearsInListAndOldTokenLinkStillWorks()
    {
        var (userId, session) = await _kit.CreateCustomerAsync();
        var email = IdentityTestData.NewEmail("link-list");
        var rawToken = NewToken();
        var orderId = await _seed.AddOrderAsync(email, null, rawToken);
        var before = await _seed.ReadOrderAsync(orderId);
        var statusEventsBefore = _factory.DomainEvents.OfType<OrderStatusChangedEvent>().Count();

        await VerifyAsync(userId, email);

        var list = await _kit.SendAsync(HttpMethod.Get, "/api/orders", _kit.NewAnonymousId(), sessionToken: session);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var codes = (await CartTestKit.ReadJsonAsync(list)).GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToList();
        Assert.Contains(before.Code, codes);

        // Link cũ trong email xác nhận đơn không kèm phiên đăng nhập vẫn mở được đơn.
        var viaToken = await _kit.SendAsync(HttpMethod.Get, $"/api/orders/{before.Code}?t={rawToken}", _kit.NewAnonymousId());
        Assert.Equal(HttpStatusCode.OK, viaToken.StatusCode);

        var after = await _seed.ReadOrderAsync(orderId);
        Assert.Equal(before.AccessTokenHash, after.AccessTokenHash);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(statusEventsBefore, _factory.DomainEvents.OfType<OrderStatusChangedEvent>().Count());
    }

    [Fact]
    // Khách không có đơn vãng lai nào cùng email thì sự kiện không đổi gì và không lỗi, kể cả khi phát hai lần.
    public async Task EmailVerified_WithoutMatchingOrders_DoesNothing()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var email = IdentityTestData.NewEmail("link-none");

        await VerifyAsync(userId, email);
        await VerifyAsync(userId, email);

        await using var commerce = _database.CreateCommerceContext();
        Assert.False(await commerce.Orders.AnyAsync(order => order.UserId == userId));
    }

    [Fact]
    // Luồng thật: đơn vãng lai tồn tại, khách đăng ký cùng email, mở link xác thực trong outbox, rồi thấy đơn trong danh sách.
    public async Task RealFlow_RegisterAndVerifyEmail_LinksGuestOrder()
    {
        var email = IdentityTestData.NewEmail("link-flow");
        var rawToken = NewToken();
        var orderId = await _seed.AddOrderAsync(email, null, rawToken);
        var order = await _seed.ReadOrderAsync(orderId);

        var register = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/auth/registrations", new { email, password = "ValidPassword123!" }, $"pk_anon={_kit.NewAnonymousId()}"));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var sessionCookie = Assert.Single(ActorHttp.ReadSetCookies(register, "pk_session"));
        var session = ActorHttp.ReadCookieValue(sessionCookie);

        // Chưa xác thực email thì đơn chưa gắn.
        var beforeVerify = await _kit.SendAsync(HttpMethod.Get, "/api/orders", _kit.NewAnonymousId(), sessionToken: session);
        Assert.Empty((await CartTestKit.ReadJsonAsync(beforeVerify)).GetProperty("data").GetProperty("items").EnumerateArray());

        var verifyToken = Assert.Single(_factory.EmailSender.MessagesTo(email)).Metadata["token"];
        var verify = await _client.SendAsync(ActorHttp.CreateJsonRequest(
            HttpMethod.Post, "/api/auth/email-verifications", new { token = verifyToken }));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var afterVerify = await _kit.SendAsync(HttpMethod.Get, "/api/orders", _kit.NewAnonymousId(), sessionToken: session);
        var codes = (await CartTestKit.ReadJsonAsync(afterVerify)).GetProperty("data").GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToList();
        Assert.Equal([order.Code], codes);
    }
}
