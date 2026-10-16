using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Api.Identity;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Identity.Contracts.Events;
using Pickory.Shared.Events;
using Pickory.Modules.Identity.Tokens;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-21: khi khách xóa tài khoản, giỏ và sổ địa chỉ bị xóa. Đánh giá và đơn hàng được ẩn danh, giữ nguyên nội dung.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class AccountDeletionCommerceDataTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly IntegrationDatabaseFixture _database;
    private readonly CartTestKit _kit;
    private readonly LinkingTestSeed _seed;

    public AccountDeletionCommerceDataTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _database = database;
        _kit = new CartTestKit(factory, database);
        _seed = new LinkingTestSeed(database);
    }

    public Task InitializeAsync() => _kit.InitializeAsync();

    public async Task DisposeAsync()
    {
        await _seed.CleanupAsync();
        await _kit.CleanupAsync();
    }

    private Task DeleteAccountEventAsync(string userId) =>
        _factory.Services.GetRequiredService<IEventDispatcher>().PublishAsync(new UserDeletedEvent(userId));

    // Đọc danh sách đánh giá công khai của sản phẩm. Trang này là nơi khách khác thấy tên người viết.
    private async Task<System.Text.Json.JsonElement> PublicReviewsAsync(string productId)
    {
        var response = await _kit.SendAsync(HttpMethod.Get, $"/api/products/{productId}/reviews", _kit.NewAnonymousId());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await CartTestKit.ReadJsonAsync(response)).GetProperty("data");
    }

    [Fact]
    // Đánh giá giữ nguyên số sao và nhận xét, `UserId` về rỗng, trang sản phẩm hiện "Khách hàng" và điểm trung bình không đổi.
    public async Task UserDeleted_AnonymizesReviewButKeepsContent()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var (otherUserId, _) = await _kit.CreateCustomerAsync();
        var product = await _kit.AddProductAsync();
        var orderId = await _seed.AddOrderAsync(IdentityTestData.NewEmail("del-review"), userId, SecureTokens.GenerateToken());
        var otherOrderId = await _seed.AddOrderAsync(IdentityTestData.NewEmail("del-review-other"), otherUserId, SecureTokens.GenerateToken());
        var reviewId = await _seed.AddReviewAsync(product.Id, orderId, userId, rating: 2, body: "Nhận xét sẽ được giữ");
        var otherReviewId = await _seed.AddReviewAsync(product.Id, otherOrderId, otherUserId, rating: 4, body: "Nhận xét của khách khác");
        var averageBefore = (await PublicReviewsAsync(product.Id)).GetProperty("summary").GetProperty("average").GetDouble();

        await DeleteAccountEventAsync(userId);

        var review = await _seed.ReadReviewAsync(reviewId);
        Assert.Null(review.UserId);
        Assert.Equal(2, review.Rating);
        Assert.Equal("Nhận xét sẽ được giữ", review.Body);
        Assert.Equal(orderId, review.OrderId);
        // Khách khác không bị ảnh hưởng.
        Assert.Equal(otherUserId, (await _seed.ReadReviewAsync(otherReviewId)).UserId);

        var publicList = await PublicReviewsAsync(product.Id);
        Assert.Equal(averageBefore, publicList.GetProperty("summary").GetProperty("average").GetDouble());
        var mine = publicList.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == reviewId);
        Assert.Equal("Khách hàng", mine.GetProperty("authorName").GetString());
    }

    [Fact]
    // Đơn hàng còn nguyên mọi trường, kể cả liên hệ, và chỉ mất liên kết tới tài khoản. Đơn của khách khác không đổi.
    public async Task UserDeleted_AnonymizesOrderButKeepsEverythingElse()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var (otherUserId, _) = await _kit.CreateCustomerAsync();
        var email = IdentityTestData.NewEmail("del-order");
        var orderId = await _seed.AddOrderAsync(email, userId, SecureTokens.GenerateToken());
        var otherOrderId = await _seed.AddOrderAsync(IdentityTestData.NewEmail("del-order-other"), otherUserId, SecureTokens.GenerateToken());
        var before = await _seed.ReadOrderAsync(orderId);

        await DeleteAccountEventAsync(userId);

        var after = await _seed.ReadOrderAsync(orderId);
        Assert.Null(after.UserId);
        Assert.Equal(before.Code, after.Code);
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.AccessTokenHash, after.AccessTokenHash);
        Assert.Equal(before.Contact.RootElement.GetRawText(), after.Contact.RootElement.GetRawText());
        Assert.Equal(otherUserId, (await _seed.ReadOrderAsync(otherOrderId)).UserId);
    }

    [Fact]
    // Giỏ và sổ địa chỉ vẫn bị xóa như COM-05, còn dữ liệu của khách khác giữ nguyên.
    public async Task UserDeleted_StillRemovesCartAndSavedAddresses()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var (otherUserId, _) = await _kit.CreateCustomerAsync();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.SeedCartAsync(userId, null, (skuId, 1));
        await _kit.SeedCartAsync(otherUserId, null, (skuId, 1));
        await _kit.AddSavedAddressAsync(userId);
        await _kit.AddSavedAddressAsync(otherUserId);

        await DeleteAccountEventAsync(userId);

        Assert.Empty(await _kit.CartsOfUserAsync(userId));
        Assert.Equal(0, await _kit.SavedAddressCountAsync(userId));
        Assert.Single(await _kit.CartsOfUserAsync(otherUserId));
        Assert.Equal(1, await _kit.SavedAddressCountAsync(otherUserId));
    }

    [Fact]
    // Phát sự kiện hai lần hoặc cho người không có dữ liệu không gây lỗi.
    public async Task UserDeleted_WithoutOrdersOrReviews_DoesNothingAndDoesNotFail()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();

        await DeleteAccountEventAsync(userId);
        await DeleteAccountEventAsync(userId);

        await using var commerce = _database.CreateCommerceContext();
        Assert.False(await commerce.Orders.AnyAsync(order => order.UserId == userId));
    }

    [Fact]
    // Hệ quả đã chấp nhận: đơn của tài khoản đã xóa thành đơn vãng lai cùng email.
    // Chủ email tạo tài khoản mới và xác thực thì các đơn này gắn vào tài khoản mới.
    public async Task DeletedAccountOrder_BecomesGuestOrderAndLinksToNewAccountOfSameEmail()
    {
        var (oldUserId, _) = await _kit.CreateCustomerAsync();
        var (newUserId, _) = await _kit.CreateCustomerAsync();
        var email = IdentityTestData.NewEmail("del-reuse");
        var orderId = await _seed.AddOrderAsync(email, oldUserId, SecureTokens.GenerateToken());

        await DeleteAccountEventAsync(oldUserId);
        Assert.Null((await _seed.ReadOrderAsync(orderId)).UserId);

        await _factory.Services.GetRequiredService<IEventDispatcher>().PublishAsync(new EmailVerifiedEvent(newUserId, email));

        Assert.Equal(newUserId, (await _seed.ReadOrderAsync(orderId)).UserId);
    }

    [Fact]
    // Cập nhật đơn lỗi giữa chừng thì cả transaction quay lui: giỏ, địa chỉ và đánh giá vẫn như cũ.
    // Test đặt trigger chặn cập nhật đúng một đơn để ép lỗi, rồi gỡ trigger sau khi kiểm.
    public async Task UserDeleted_WhenOrderUpdateFails_RollsBackEverything()
    {
        var (userId, _) = await _kit.CreateCustomerAsync();
        var product = await _kit.AddProductAsync();
        await _kit.SeedCartAsync(userId, null, (product.SkuIds[0], 1));
        await _kit.AddSavedAddressAsync(userId);
        var orderId = await _seed.AddOrderAsync(IdentityTestData.NewEmail("del-rollback"), userId, SecureTokens.GenerateToken());
        var reviewId = await _seed.AddReviewAsync(product.Id, orderId, userId, rating: 3, body: "Còn nguyên");
        var triggerName = "fail_order_update_" + Guid.NewGuid().ToString("N")[..8];

        await using (var commerce = _database.CreateCommerceContext())
        {
            // Hàm và trigger chỉ tác động tới đơn của test này, nên test chạy song song trong cùng DB không bị ảnh hưởng.
            var createSql =
                "CREATE FUNCTION " + triggerName + "() RETURNS trigger AS $$ " +
                "BEGIN IF OLD.id = '" + orderId + "' THEN RAISE EXCEPTION 'Lỗi cố ý của test'; END IF; RETURN NEW; END $$ LANGUAGE plpgsql; " +
                "CREATE TRIGGER " + triggerName + " BEFORE UPDATE ON orders FOR EACH ROW EXECUTE FUNCTION " + triggerName + "();";
            await commerce.Database.ExecuteSqlRawAsync(createSql);
        }

        try
        {
            // Bộ điều phối ghi log và nuốt lỗi của handler, nên lời gọi này không ném ngoại lệ.
            await DeleteAccountEventAsync(userId);

            Assert.Single(await _kit.CartsOfUserAsync(userId));
            Assert.Equal(1, await _kit.SavedAddressCountAsync(userId));
            Assert.Equal(userId, (await _seed.ReadReviewAsync(reviewId)).UserId);
            Assert.Equal(userId, (await _seed.ReadOrderAsync(orderId)).UserId);
        }
        finally
        {
            await using var commerce = _database.CreateCommerceContext();
            var dropSql = "DROP TRIGGER IF EXISTS " + triggerName + " ON orders; DROP FUNCTION IF EXISTS " + triggerName + "();";
            await commerce.Database.ExecuteSqlRawAsync(dropSql);
        }
    }

    [Fact]
    // Luồng thật: khách tự xóa tài khoản bằng route của identity thì giỏ, địa chỉ biến mất, còn đánh giá và đơn được ẩn danh.
    public async Task RealFlow_DeleteAccount_AnonymizesReviewAndOrder()
    {
        var (userId, sessionToken) = await _kit.CreateCustomerAsync();
        var product = await _kit.AddProductAsync();
        await _kit.SeedCartAsync(userId, null, (product.SkuIds[0], 1));
        await _kit.AddSavedAddressAsync(userId);
        var orderId = await _seed.AddOrderAsync(IdentityTestData.NewEmail("del-flow"), userId, SecureTokens.GenerateToken());
        var reviewId = await _seed.AddReviewAsync(product.Id, orderId, userId, rating: 5, body: "Tốt");

        var response = await _kit.SendAsync(
            HttpMethod.Delete, "/api/me", _kit.NewAnonymousId(),
            new { password = IdentityTestData.DefaultPassword }, sessionToken);

        Assert.True(response.IsSuccessStatusCode, $"Xóa tài khoản trả {(int)response.StatusCode}");
        Assert.Empty(await _kit.CartsOfUserAsync(userId));
        Assert.Equal(0, await _kit.SavedAddressCountAsync(userId));
        Assert.Null((await _seed.ReadOrderAsync(orderId)).UserId);
        var review = await _seed.ReadReviewAsync(reviewId);
        Assert.Null(review.UserId);
        Assert.Equal("Tốt", review.Body);
    }
}
