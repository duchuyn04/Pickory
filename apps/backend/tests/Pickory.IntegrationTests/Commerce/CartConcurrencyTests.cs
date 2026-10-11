using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Entities;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-04 quyết định 4 và 5: request ghi đồng thời không tạo hai giỏ và không làm request lỗi 500.
// Mỗi test lặp nhiều vòng với mã ẩn danh mới, và kết quả đúng phải giống nhau ở mọi vòng.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CartConcurrencyTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private const int Rounds = 8;
    private const int ParallelRequests = 6;

    private readonly CartTestKit _kit;

    public CartConcurrencyTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _kit = new CartTestKit(factory, database);
    }

    public Task InitializeAsync() => _kit.InitializeAsync();

    public Task DisposeAsync() => _kit.CleanupAsync();

    // Chạy các request cùng lúc. Cổng chặn giữ mọi request tới khi tất cả đã sẵn sàng, để chúng thật sự chồng nhau.
    private static async Task<HttpResponseMessage[]> RunTogetherAsync(IReadOnlyList<Func<Task<HttpResponseMessage>>> requests)
    {
        var gate = new TaskCompletionSource();
        var tasks = requests.Select(async send =>
        {
            await gate.Task;
            return await send();
        }).ToArray();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    [Fact]
    // Quyết định 4: nhiều POST đầu tiên cùng mã ẩn danh chỉ tạo đúng một giỏ, và không request nào lỗi 500.
    public async Task ParallelFirstPosts_SameAnonymousId_CreateExactlyOneCart()
    {
        var skuIds = await _kit.AddSkusAsync(ParallelRequests);

        for (var round = 0; round < Rounds; round++)
        {
            var anonymousId = _kit.NewAnonymousId();

            var responses = await RunTogetherAsync(
                skuIds.Select<string, Func<Task<HttpResponseMessage>>>(
                    skuId => () => _kit.AddAsync(anonymousId, skuId)).ToList());

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var carts = await _kit.CartsOfAnonymousAsync(anonymousId);
            var cart = Assert.Single(carts);
            Assert.Equal(skuIds.Order(StringComparer.Ordinal), cart.Items.Select(item => item.SkuId).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    // Quyết định 5: nhiều POST cùng SKU cùng lúc không trùng khóa `(cart_id, sku_id)`. Số lượng cuối là tổng.
    public async Task ParallelPosts_SameSku_SumQuantityWithoutServerError()
    {
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];
        await _kit.SetLimitsAsync(maxQuantityPerSku: 100, maxCartLines: 20);

        for (var round = 0; round < Rounds; round++)
        {
            var anonymousId = _kit.NewAnonymousId();

            var responses = await RunTogetherAsync(
                Enumerable.Range(0, ParallelRequests)
                    .Select<int, Func<Task<HttpResponseMessage>>>(_ => () => _kit.AddAsync(anonymousId, skuId, quantity: 2))
                    .ToList());

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var cart = Assert.Single(await _kit.CartsOfAnonymousAsync(anonymousId));
            var item = Assert.Single(cart.Items);
            Assert.Equal(ParallelRequests * 2, item.Quantity);
            Assert.Equal(ParallelRequests, _kit.AddedEventsOf(anonymousId).Count);
        }
    }

    [Fact]
    // Giới hạn số lượng không bị vượt khi hai POST cùng SKU chạy đồng thời: đúng một request thắng.
    public async Task ParallelPosts_SameSku_NeverExceedQuantityLimit()
    {
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];

        for (var round = 0; round < Rounds; round++)
        {
            var anonymousId = _kit.NewAnonymousId();

            var responses = await RunTogetherAsync(
                Enumerable.Range(0, 2)
                    .Select<int, Func<Task<HttpResponseMessage>>>(_ => () => _kit.AddAsync(anonymousId, skuId, quantity: 3))
                    .ToList());

            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.UnprocessableEntity));
            var cart = Assert.Single(await _kit.CartsOfAnonymousAsync(anonymousId));
            Assert.Equal(3, Assert.Single(cart.Items).Quantity);
        }
    }

    [Fact]
    // Giới hạn số dòng không bị vượt khi các POST khác SKU chạy đồng thời: số dòng cuối đúng bằng giới hạn.
    public async Task ParallelPosts_DifferentSkus_NeverExceedLineLimit()
    {
        await _kit.SetLimitsAsync(maxQuantityPerSku: 5, maxCartLines: 2);
        var skuIds = await _kit.AddSkusAsync(ParallelRequests);

        for (var round = 0; round < Rounds; round++)
        {
            var anonymousId = _kit.NewAnonymousId();

            var responses = await RunTogetherAsync(
                skuIds.Select<string, Func<Task<HttpResponseMessage>>>(
                    skuId => () => _kit.AddAsync(anonymousId, skuId)).ToList());

            Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            Assert.Equal(ParallelRequests - 2, responses.Count(response => response.StatusCode == HttpStatusCode.UnprocessableEntity));
            var cart = Assert.Single(await _kit.CartsOfAnonymousAsync(anonymousId));
            Assert.Equal(2, cart.Items.Count);
        }
    }

    [Fact]
    // Quyết định 4: DB tự từ chối hai giỏ cùng `AnonymousId`. Đây là lớp bảo vệ cuối khi mã ứng dụng sai.
    public async Task Database_RejectsSecondCartWithSameAnonymousId()
    {
        var anonymousId = _kit.NewAnonymousId();
        await using (var first = _kit.Database.CreateCommerceContext())
        {
            first.Carts.Add(new Cart { AnonymousId = anonymousId, UpdatedAt = DateTime.UtcNow });
            await first.SaveChangesAsync();
        }

        await using var second = _kit.Database.CreateCommerceContext();
        second.Carts.Add(new Cart { AnonymousId = anonymousId, UpdatedAt = DateTime.UtcNow });
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

        var postgres = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("carts_anonymousId_key", postgres.ConstraintName);
    }
}
