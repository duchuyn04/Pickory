using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Commerce;

// COM-04 tiêu chí 6: sự kiện giỏ hàng chỉ phát sau commit.
// Handler thử đọc DB bằng kết nối riêng ngay lúc nhận sự kiện, và kết nối này không thấy dữ liệu chưa commit.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class CartEventTimingTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly PickoryWebApplicationFactory _factory;
    private readonly CartTestKit _kit;

    public CartEventTimingTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _factory = factory;
        _kit = new CartTestKit(factory, database);
    }

    public Task InitializeAsync() => _kit.InitializeAsync();

    public Task DisposeAsync() => _kit.CleanupAsync();

    // Ghi lại việc dòng giỏ có thấy được hay không tại lúc từng handler nhận sự kiện.
    private sealed class CommittedStateProbe
    {
        public ConcurrentQueue<bool> RowVisibleWhenAdded { get; } = new();

        public ConcurrentQueue<bool> RowVisibleWhenRemoved { get; } = new();
    }

    private sealed class AddedProbeHandler(IServiceScopeFactory scopes, CommittedStateProbe probe)
        : IDomainEventHandler<CartItemAddedEvent>
    {
        public async Task HandleAsync(CartItemAddedEvent @event, CancellationToken cancellationToken = default)
        {
            probe.RowVisibleWhenAdded.Enqueue(await RowExistsAsync(scopes, @event.SkuId, @event.AnonymousId!, cancellationToken));
        }
    }

    private sealed class RemovedProbeHandler(IServiceScopeFactory scopes, CommittedStateProbe probe)
        : IDomainEventHandler<CartItemRemovedEvent>
    {
        public async Task HandleAsync(CartItemRemovedEvent @event, CancellationToken cancellationToken = default)
        {
            probe.RowVisibleWhenRemoved.Enqueue(await RowExistsAsync(scopes, @event.SkuId, @event.AnonymousId!, cancellationToken));
        }
    }

    private static async Task<bool> RowExistsAsync(
        IServiceScopeFactory scopes, string skuId, string anonymousId, CancellationToken cancellationToken)
    {
        // Scope mới có `DbContext` và kết nối riêng, nên chỉ đọc được dữ liệu đã commit.
        await using var scope = scopes.CreateAsyncScope();
        var commerce = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        return await commerce.CartItems.AsNoTracking()
            .AnyAsync(item => item.SkuId == skuId && item.Cart!.AnonymousId == anonymousId, cancellationToken);
    }

    [Fact]
    public async Task AddedAndRemovedEvents_AreHandledOnlyAfterTheChangeIsCommitted()
    {
        var probe = new CommittedStateProbe();
        using var probedFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(probe);
            services.AddSingleton<IDomainEventHandler<CartItemAddedEvent>, AddedProbeHandler>();
            services.AddSingleton<IDomainEventHandler<CartItemRemovedEvent>, RemovedProbeHandler>();
        }));
        var client = probedFactory.CreateClient();
        var anonymousId = _kit.NewAnonymousId();
        var skuId = (await _kit.AddProductAsync()).SkuIds[0];

        var addResponse = await Send(client, HttpMethod.Post, "/api/cart/items", anonymousId, new { skuId, quantity = 1 });
        var removeResponse = await Send(client, HttpMethod.Delete, $"/api/cart/items/{skuId}", anonymousId, null);

        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        // Lúc nhận sự kiện thêm, dòng đã commit nên thấy được. Lúc nhận sự kiện xóa, dòng đã biến mất.
        Assert.Equal([true], probe.RowVisibleWhenAdded);
        Assert.Equal([false], probe.RowVisibleWhenRemoved);
    }

    private static Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string anonymousId, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"pk_anon={anonymousId}");
        request.Headers.Add("Origin", "http://localhost:5173");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }
}
