using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Carts;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Modules.Identity.Entities;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-05: gộp giỏ vãng lai vào giỏ tài khoản khi nhận `UserLinkedEvent`, và dọn dữ liệu khi nhận `UserDeletedEvent`.
// Test phát sự kiện qua dispatcher thật, nên cả việc đăng ký handler trong module cũng được kiểm.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CartMergeTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const int Rounds = 6;

    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;

    public CartMergeTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new CartTestKit(factory, database);
    }

    public Task InitializeAsync() => _kit.InitializeAsync();

    public Task DisposeAsync() => _kit.CleanupAsync();

    private Task LinkAsync(string anonymousId, string userId) =>
        _factory.Services.GetRequiredService<IEventDispatcher>()
            .PublishAsync(new UserLinkedEvent(anonymousId, userId));

    private static Dictionary<string, int> QuantitiesOf(IEnumerable<CartItem> items) =>
        items.ToDictionary(item => item.SkuId, item => item.Quantity);

    [Fact]
    // Quyết định 3: không có giỏ vãng lai thì không làm gì và không tạo giỏ tài khoản.
    public async Task Link_WithoutGuestCart_DoesNothing()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();

        await LinkAsync(_kit.NewAnonymousId(), userId);

        Assert.Empty(await _kit.CartsOfUserAsync(userId));
    }

    [Fact]
    // Quyết định 3: giỏ vãng lai rỗng bị xóa, và không tạo giỏ tài khoản rỗng.
    public async Task Link_EmptyGuestCart_IsDeletedWithoutCreatingAccountCart()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        await _kit.SeedCartAsync(null, anonymousId);

        await LinkAsync(anonymousId, userId);

        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
        Assert.Empty(await _kit.CartsOfUserAsync(userId));
    }

    [Fact]
    // Quyết định 4: chưa có giỏ tài khoản thì giỏ vãng lai thành giỏ tài khoản, số lượng kẹp theo giới hạn.
    public async Task Link_GuestCartOnly_BecomesAccountCartWithClampedQuantity()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var skuIds = await _kit.AddSkusAsync(2);
        await _kit.SeedCartAsync(null, anonymousId, (skuIds[0], 8), (skuIds[1], 2));

        await LinkAsync(anonymousId, userId);

        var cart = Assert.Single(await _kit.CartsOfUserAsync(userId));
        Assert.Null(cart.AnonymousId);
        var quantities = QuantitiesOf(cart.Items);
        Assert.Equal(5, quantities[skuIds[0]]);
        Assert.Equal(2, quantities[skuIds[1]]);
        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
    }

    [Fact]
    // Tiêu chí 1 và quyết định 5: cùng SKU cộng số lượng nhưng không vượt giới hạn. SKU mới được thêm và giỏ vãng lai bị xóa.
    public async Task Link_BothCarts_SumsSameSkuWithinLimit_AddsNewSku_AndDeletesGuestCart()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var skuIds = await _kit.AddSkusAsync(4);
        await _kit.SeedCartAsync(userId, null, (skuIds[0], 2), (skuIds[1], 4));
        await _kit.SeedCartAsync(null, anonymousId, (skuIds[0], 2), (skuIds[1], 3), (skuIds[2], 1));

        await LinkAsync(anonymousId, userId);

        var cart = Assert.Single(await _kit.CartsOfUserAsync(userId));
        var quantities = QuantitiesOf(cart.Items);
        Assert.Equal(4, quantities[skuIds[0]]);
        Assert.Equal(5, quantities[skuIds[1]]);
        Assert.Equal(1, quantities[skuIds[2]]);
        Assert.Equal(3, quantities.Count);
        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
    }

    [Fact]
    // Quyết định 5: dòng mới vượt giới hạn số dòng bị bỏ theo thứ tự `skuId`, dòng cộng vào SKU có sẵn vẫn được giữ.
    public async Task Link_LineLimit_DropsExcessNewLinesInSkuOrder()
    {
        await _kit.SetLimitsAsync(maxQuantityPerSku: 5, maxCartLines: 3);
        var (userId, _) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var skuIds = (await _kit.AddSkusAsync(4)).Order(StringComparer.Ordinal).ToList();
        await _kit.SeedCartAsync(userId, null, (skuIds[3], 1));
        await _kit.SeedCartAsync(null, anonymousId, (skuIds[0], 1), (skuIds[1], 1), (skuIds[2], 1), (skuIds[3], 1));

        await LinkAsync(anonymousId, userId);

        var cart = Assert.Single(await _kit.CartsOfUserAsync(userId));
        var quantities = QuantitiesOf(cart.Items);
        Assert.Equal(3, quantities.Count);
        // Giỏ tài khoản đã có skuIds[3]. Hai dòng đầu theo thứ tự `skuId` được thêm, dòng thứ ba bị bỏ.
        Assert.Equal(2, quantities[skuIds[3]]);
        Assert.Equal(1, quantities[skuIds[0]]);
        Assert.Equal(1, quantities[skuIds[1]]);
        Assert.DoesNotContain(skuIds[2], quantities.Keys);
    }

    [Fact]
    // Quyết định 7: gộp giỏ không gọi catalog, nên dòng của SKU không còn trong catalog vẫn được gộp.
    public async Task Link_LineOfUnknownOrUnavailableSku_IsStillMerged()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var disabledSku = (await _kit.AddProductAsync(enabled: false)).SkuIds[0];
        await _kit.SeedCartAsync(null, anonymousId, (disabledSku, 1), ("sku-da-bi-go", 2));

        await LinkAsync(anonymousId, userId);

        var quantities = QuantitiesOf(Assert.Single(await _kit.CartsOfUserAsync(userId)).Items);
        Assert.Equal(1, quantities[disabledSku]);
        Assert.Equal(2, quantities["sku-da-bi-go"]);
    }

    [Fact]
    // Quyết định 6: gộp giỏ không phải thao tác thêm hoặc xóa của khách, nên không phát sự kiện giỏ.
    public async Task Link_EmitsNoCartEvents()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.SeedCartAsync(userId, null, (skuId, 1));
        await _kit.SeedCartAsync(null, anonymousId, (skuId, 1));

        await LinkAsync(anonymousId, userId);

        Assert.Empty(_kit.AddedEventsOf(anonymousId));
        Assert.Empty(_kit.RemovedEventsOf(anonymousId));
        Assert.DoesNotContain(_factory.DomainEvents.OfType<CartItemAddedEvent>(), item => item.UserId == userId);
        Assert.DoesNotContain(_factory.DomainEvents.OfType<CartItemRemovedEvent>(), item => item.UserId == userId);
    }

    [Fact]
    // Tiêu chí 3 và quyết định 8: nhận cùng sự kiện lần hai không cộng số lượng lần hai.
    public async Task Link_SameEventTwiceInSequence_DoesNotSumTwice()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.SeedCartAsync(userId, null, (skuId, 1));
        await _kit.SeedCartAsync(null, anonymousId, (skuId, 2));

        await LinkAsync(anonymousId, userId);
        await LinkAsync(anonymousId, userId);

        var quantities = QuantitiesOf(Assert.Single(await _kit.CartsOfUserAsync(userId)).Items);
        Assert.Equal(3, quantities[skuId]);
    }

    [Fact]
    // Quyết định 2 và 8: nhiều lần gộp đồng thời cho kết quả giống một lần, và không deadlock hay lỗi.
    public async Task Link_SameEventInParallel_MergesExactlyOnce()
    {
        var skuIds = await _kit.AddSkusAsync(3);

        for (var round = 0; round < Rounds; round++)
        {
            var (userId, _) = await _kit.CreateCustomerAsync();
            var anonymousId = _kit.NewAnonymousId();
            await _kit.SeedCartAsync(userId, null, (skuIds[0], 1));
            await _kit.SeedCartAsync(null, anonymousId, (skuIds[0], 2), (skuIds[1], 1), (skuIds[2], 4));

            // Cổng giữ mọi lần gộp tới khi tất cả đã sẵn sàng, để chúng thật sự chồng nhau.
            var gate = new TaskCompletionSource();
            // Gọi thẳng service thay vì dispatcher. Dispatcher nuốt lỗi handler, nên lỗi tranh chấp sẽ bị che.
            var runs = Enumerable.Range(0, 4).Select(async _ =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var mergeService = scope.ServiceProvider.GetRequiredService<CartMergeService>();
                await gate.Task;
                await mergeService.MergeGuestCartAsync(anonymousId, userId, CancellationToken.None);
            }).ToArray();
            gate.SetResult();
            await Task.WhenAll(runs);

            var quantities = QuantitiesOf(Assert.Single(await _kit.CartsOfUserAsync(userId)).Items);
            Assert.Equal(3, quantities[skuIds[0]]);
            Assert.Equal(1, quantities[skuIds[1]]);
            Assert.Equal(4, quantities[skuIds[2]]);
            Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
        }
    }

    [Fact]
    // Luồng thật: khách vãng lai thêm giỏ bằng route, đăng nhập bằng route của identity, rồi xem giỏ thấy dòng đã gộp.
    public async Task RealFlow_GuestAddsToCart_ThenLogsIn_CartIsMergedIntoAccount()
    {
        var email = IdentityTestData.NewEmail("cart-merge");
        var user = await IdentityTestData.CreateUserAsync(
            _database, email, UserKind.Customer, "customer", emailVerified: true);
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync(price: 120_000)).SkuIds[0];
        await _kit.AddAsync(anonymousId, skuId, quantity: 2);

        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sessions")
        {
            Content = JsonContent.Create(new { email, password = IdentityTestData.DefaultPassword }),
        };
        login.Headers.Add("Cookie", $"pk_anon={anonymousId}");
        login.Headers.Add("Origin", "http://localhost:5173");
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.Created, loginResponse.StatusCode);
        var sessionToken = ActorHttp.ReadSessionToken(loginResponse);

        // Dùng mã ẩn danh khác để chứng minh giỏ đi theo tài khoản chứ không theo cookie ẩn danh.
        var cart = await CartTestKit.ReadCartAsync(await _kit.GetCartAsync(_kit.NewAnonymousId(), sessionToken));

        var line = CartTestKit.LineOf(cart, skuId);
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Equal(240_000, cart.GetProperty("subtotal").GetInt32());
        Assert.Empty(await _kit.CartsOfAnonymousAsync(anonymousId));
        Assert.Single(await _kit.CartsOfUserAsync(user.Id));
    }

    [Fact]
    // Tiêu chí 4 và quyết định 9: xóa tài khoản xóa giỏ và mọi địa chỉ đã lưu của người đó. Dữ liệu người khác giữ nguyên.
    public async Task UserDeleted_RemovesCartAndSavedAddresses_OfThatUserOnly()
    {
        var (deletedUserId, _) = await _kit.CreateCustomerAsync();
        var (otherUserId, _) = await _kit.CreateCustomerAsync();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.SeedCartAsync(deletedUserId, null, (skuId, 2));
        await _kit.SeedCartAsync(otherUserId, null, (skuId, 1));
        await _kit.AddSavedAddressAsync(deletedUserId);
        await _kit.AddSavedAddressAsync(deletedUserId);
        await _kit.AddSavedAddressAsync(otherUserId);

        await _factory.Services.GetRequiredService<IEventDispatcher>().PublishAsync(new UserDeletedEvent(deletedUserId));

        Assert.Empty(await _kit.CartsOfUserAsync(deletedUserId));
        Assert.Equal(0, await _kit.SavedAddressCountAsync(deletedUserId));
        Assert.Single(await _kit.CartsOfUserAsync(otherUserId));
        Assert.Equal(1, await _kit.SavedAddressCountAsync(otherUserId));
    }

    [Fact]
    // Quyết định 9: người dùng không có dữ liệu thì không lỗi, kể cả khi nhận sự kiện hai lần.
    public async Task UserDeleted_WithoutData_DoesNothingAndDoesNotFail()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var dispatcher = _factory.Services.GetRequiredService<IEventDispatcher>();

        await dispatcher.PublishAsync(new UserDeletedEvent(userId));
        await dispatcher.PublishAsync(new UserDeletedEvent(userId));

        Assert.Empty(await _kit.CartsOfUserAsync(userId));
    }

    [Fact]
    // Luồng thật: khách tự xóa tài khoản bằng route của identity thì giỏ và địa chỉ đã lưu biến mất.
    public async Task RealFlow_DeleteAccount_RemovesCartAndSavedAddresses()
    {
        var (userId, sessionToken) = await _kit.CreateCustomerAsync();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.SeedCartAsync(userId, null, (skuId, 1));
        await _kit.AddSavedAddressAsync(userId);

        var response = await _kit.SendAsync(
            HttpMethod.Delete, "/api/me", _kit.NewAnonymousId(),
            new { password = IdentityTestData.DefaultPassword }, sessionToken);

        Assert.True(response.IsSuccessStatusCode, $"Xóa tài khoản trả {(int)response.StatusCode}");
        Assert.Empty(await _kit.CartsOfUserAsync(userId));
        Assert.Equal(0, await _kit.SavedAddressCountAsync(userId));
    }
}
