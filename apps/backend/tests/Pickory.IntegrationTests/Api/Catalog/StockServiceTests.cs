using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pickory.IntegrationTests.Infrastructure;
using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Modules.Catalog.Entities;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.IntegrationTests.Api.Catalog;

// CAT-08: `IStockService` giữ hàng và trả hàng, gồm cả trường hợp bên gọi sở hữu transaction.
[Collection(IntegrationDatabaseCollection.Name)]
public sealed class StockServiceTests : IClassFixture<PickoryWebApplicationFactory>, IAsyncLifetime
{
    private readonly IntegrationDatabaseFixture _database;
    private readonly PickoryWebApplicationFactory _factory;
    private readonly StockTestData _data;
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];

    public StockServiceTests(PickoryWebApplicationFactory factory, IntegrationDatabaseFixture database)
    {
        _database = database;
        _factory = factory;
        _data = new StockTestData(database, _suffix);
        // Tạo host trước để `Services` sẵn sàng khi test gọi `IServiceScopeFactory`.
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public async Task InitializeAsync() => await _data.InitializeAsync();

    public async Task DisposeAsync()
    {
        await StockInvariant.AssertHoldsAsync(_database);
        await _data.CleanupAsync();
    }

    private string NewOrderId() => $"order-{_suffix}-{Guid.NewGuid():N}";

    private async Task<ReserveResult> ReserveAsync(string orderId, params StockItem[] items)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IStockService>();
        return await service.ReserveAsync(orderId, items, null, CancellationToken.None);
    }

    private async Task ReleaseAsync(string orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IStockService>();
        await service.ReleaseAsync(orderId, null, CancellationToken.None);
    }

    private IReadOnlyList<StockChangedEvent> StockEventsOf(params string[] skuIds) =>
        _factory.DomainEvents.OfType<StockChangedEvent>().Where(item => skuIds.Contains(item.SkuId)).ToList();

    private async Task<IReadOnlyList<InventoryMovement>> MovementsOfTypeAsync(string skuId, InventoryMovementType type) =>
        (await _data.MovementsOfAsync(skuId)).Where(movement => movement.Type == type).ToList();

    [Fact]
    // 20 đơn cùng giữ 1 cái của SKU còn 5. Đúng 5 đơn thành công vì mỗi giao dịch chỉ trừ khi còn đủ hàng.
    public async Task Reserve_TwentyConcurrentOrdersForFiveInStock_SucceedsExactlyFiveTimes()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5));
        var skuId = product.SkuIds[0];

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => ReserveAsync(NewOrderId(), new StockItem(skuId, 1)))));

        Assert.Equal(5, results.Count(result => result.Ok));
        Assert.All(results.Where(result => !result.Ok), result =>
            Assert.Equal(new StockShortage(skuId, 1, 0), Assert.Single(result.Shortages)));
        Assert.Equal(0, await _data.StockOfAsync(skuId));
        Assert.Equal(5, (await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderReserve)).Count);
        Assert.Equal(5, StockEventsOf(skuId).Count);
    }

    [Fact]
    // Mười lời gọi cùng `orderId` chạy đồng thời chỉ ghi một movement. Khóa theo `orderId` buộc chúng chạy nối tiếp.
    public async Task Reserve_ConcurrentCallsWithSameOrderId_WriteOneMovement()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5));
        var skuId = product.SkuIds[0];
        var orderId = NewOrderId();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => ReserveAsync(orderId, new StockItem(skuId, 1)))));

        Assert.All(results, result => Assert.True(result.Ok));
        Assert.Equal(4, await _data.StockOfAsync(skuId));
        Assert.Single(await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderReserve));
        Assert.Single(StockEventsOf(skuId));
    }

    [Fact]
    // Đơn có 2 SKU và 1 SKU thiếu thì không SKU nào bị trừ, không ghi movement, và báo đúng số cần và số còn.
    public async Task Reserve_OneSkuShort_ChangesNothingAndReportsShortage()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(3), new StockTestData.SkuSeed(1));
        var enough = product.SkuIds[0];
        var short1 = product.SkuIds[1];

        var result = await ReserveAsync(NewOrderId(), new StockItem(enough, 2), new StockItem(short1, 2));

        Assert.False(result.Ok);
        Assert.Equal(new StockShortage(short1, 2, 1), Assert.Single(result.Shortages));
        Assert.Equal(3, await _data.StockOfAsync(enough));
        Assert.Equal(1, await _data.StockOfAsync(short1));
        Assert.Empty(await MovementsOfTypeAsync(enough, InventoryMovementType.OrderReserve));
        Assert.Empty(await MovementsOfTypeAsync(short1, InventoryMovementType.OrderReserve));
        Assert.Empty(StockEventsOf(enough, short1));
    }

    [Fact]
    // Hai SKU cùng thiếu thì danh sách báo đủ cả hai, sắp theo `skuId`.
    public async Task Reserve_SeveralSkusShort_ReportsAllOfThem()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(1), new StockTestData.SkuSeed(0));

        var result = await ReserveAsync(NewOrderId(),
            new StockItem(product.SkuIds[0], 5), new StockItem(product.SkuIds[1], 1));

        Assert.False(result.Ok);
        var expected = new[]
        {
            new StockShortage(product.SkuIds[0], 5, 1),
            new StockShortage(product.SkuIds[1], 1, 0),
        }.OrderBy(shortage => shortage.SkuId, StringComparer.Ordinal);
        Assert.Equal(expected, result.Shortages);
    }

    [Fact]
    // SKU không tồn tại bị báo thiếu với 0 cái, và không SKU nào của đơn bị trừ.
    public async Task Reserve_UnknownSku_ReportsShortageWithZeroAvailable()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(3));
        var ghost = Guid.NewGuid().ToString("N");

        var result = await ReserveAsync(NewOrderId(), new StockItem(product.SkuIds[0], 1), new StockItem(ghost, 1));

        Assert.False(result.Ok);
        Assert.Equal(new StockShortage(ghost, 1, 0), Assert.Single(result.Shortages));
        Assert.Equal(3, await _data.StockOfAsync(product.SkuIds[0]));
    }

    [Fact]
    // Giữ hàng lần hai cùng `orderId` trả `Ok`. Tồn kho, movement và sự kiện không đổi.
    public async Task Reserve_SameOrderTwice_IsIdempotent()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5));
        var skuId = product.SkuIds[0];
        var orderId = NewOrderId();

        var first = await ReserveAsync(orderId, new StockItem(skuId, 2));
        var eventsAfterFirst = StockEventsOf(skuId).Count;
        var second = await ReserveAsync(orderId, new StockItem(skuId, 2));

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.Equal(3, await _data.StockOfAsync(skuId));
        Assert.Single(await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderReserve));
        Assert.Equal(1, eventsAfterFirst);
        Assert.Single(StockEventsOf(skuId));
    }

    [Fact]
    // Hai mục cùng SKU trong một đơn được gộp thành một movement.
    public async Task Reserve_DuplicateItems_AreMergedIntoOneMovement()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5));
        var skuId = product.SkuIds[0];

        var result = await ReserveAsync(NewOrderId(), new StockItem(skuId, 1), new StockItem(skuId, 2));

        Assert.True(result.Ok);
        Assert.Equal(2, await _data.StockOfAsync(skuId));
        var movement = Assert.Single(await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderReserve));
        Assert.Equal(-3, movement.Delta);
    }

    [Fact]
    // Trả hàng hai lần cùng `orderId` chỉ cộng lại hàng một lần. Lần hai không phát sự kiện.
    public async Task Release_Twice_RestoresStockOnce()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5));
        var skuId = product.SkuIds[0];
        var orderId = NewOrderId();
        await ReserveAsync(orderId, new StockItem(skuId, 2));

        await ReleaseAsync(orderId);
        var eventsAfterFirstRelease = StockEventsOf(skuId).Count;
        await ReleaseAsync(orderId);

        Assert.Equal(5, await _data.StockOfAsync(skuId));
        var release = Assert.Single(await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderRelease));
        Assert.Equal(2, release.Delta);
        Assert.Equal(2, eventsAfterFirstRelease);
        Assert.Equal(2, StockEventsOf(skuId).Count);
    }

    [Fact]
    // Số dư bằng không cho phép giữ lại hàng của đơn hết hạn mà không cần đổi `orderId`.
    public async Task Reserve_AfterRelease_SameOrderId_HoldsStockAgain()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5));
        var skuId = product.SkuIds[0];
        var orderId = NewOrderId();
        await ReserveAsync(orderId, new StockItem(skuId, 2));
        await ReleaseAsync(orderId);

        var again = await ReserveAsync(orderId, new StockItem(skuId, 2));

        Assert.True(again.Ok);
        Assert.Equal(3, await _data.StockOfAsync(skuId));
        Assert.Equal(2, (await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderReserve)).Count);
    }

    [Fact]
    public async Task ReserveRelease_TwoCyclesWithRepeatedCalls_RestoresEverySku()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5), new StockTestData.SkuSeed(7));
        var orderId = NewOrderId();
        var items = new[] { new StockItem(product.SkuIds[0], 2), new StockItem(product.SkuIds[1], 3) };
        // Hai chu kỳ bảo vệ số dư theo từng SKU, không dựa vào sự tồn tại của movement cũ.
        for (var cycle = 0; cycle < 2; cycle++)
        {
            Assert.True((await ReserveAsync(orderId, items)).Ok);
            Assert.True((await ReserveAsync(orderId, items)).Ok);
            Assert.Equal(3, await _data.StockOfAsync(product.SkuIds[0]));
            Assert.Equal(4, await _data.StockOfAsync(product.SkuIds[1]));
            // Trả lặp phải giữ kho ban đầu, kể cả sau khi đơn đã được giữ lại.
            await ReleaseAsync(orderId);
            await ReleaseAsync(orderId);
            Assert.Equal(5, await _data.StockOfAsync(product.SkuIds[0]));
            Assert.Equal(7, await _data.StockOfAsync(product.SkuIds[1]));
        }
        foreach (var skuId in product.SkuIds)
        {
            Assert.Equal(2, (await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderReserve)).Count);
            Assert.Equal(2, (await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderRelease)).Count);
        }
    }

    [Fact]
    public async Task ReserveRelease_ConcurrentSameOrderAcrossCycles_ChangesStockOnce()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5));
        var skuId = product.SkuIds[0];
        var orderId = NewOrderId();
        // Hàng rào đợi hai transaction mở xong, không dùng thời gian ngủ để tạo tranh khóa.
        async Task RunPairAsync(bool reserve)
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var participants = 0;
            async Task RunAsync()
            {
                await using var caller = await BeginCallerAsync();
                if (Interlocked.Increment(ref participants) == 2)
                {
                    ready.SetResult();
                }
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
                using var scope = _factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IStockService>();
                // Cùng đơn và cùng transaction thật kiểm tính lặp sau khi bỏ unique index.
                if (reserve)
                {
                    Assert.True((await service.ReserveAsync(orderId, [new StockItem(skuId, 2)], caller.AsCaller(), CancellationToken.None)).Ok);
                }
                else
                {
                    await service.ReleaseAsync(orderId, caller.AsCaller(), CancellationToken.None);
                }
                await caller.Transaction.CommitAsync();
                await caller.Events.FlushAsync(CancellationToken.None);
            }
            await Task.WhenAll(RunAsync(), RunAsync());
        }
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await RunPairAsync(reserve: true);
            Assert.Equal(3, await _data.StockOfAsync(skuId));
            await RunPairAsync(reserve: false);
            Assert.Equal(5, await _data.StockOfAsync(skuId));
        }
        Assert.Equal(2, (await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderReserve)).Count);
        Assert.Equal(2, (await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderRelease)).Count);
    }

    [Fact]
    // Trả hàng cho đơn chưa từng giữ hàng không làm gì.
    public async Task Release_OrderThatNeverReserved_DoesNothing()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5));
        var skuId = product.SkuIds[0];

        await ReleaseAsync(NewOrderId());

        Assert.Equal(5, await _data.StockOfAsync(skuId));
        Assert.Empty(await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderRelease));
        Assert.Empty(StockEventsOf(skuId));
    }

    [Fact]
    // Đơn có nhiều SKU được trả lại đủ từng SKU.
    public async Task Release_MultiSkuOrder_RestoresEverySku()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(4), new StockTestData.SkuSeed(6));
        var orderId = NewOrderId();
        await ReserveAsync(orderId, new StockItem(product.SkuIds[0], 4), new StockItem(product.SkuIds[1], 1));

        await ReleaseAsync(orderId);

        Assert.Equal(4, await _data.StockOfAsync(product.SkuIds[0]));
        Assert.Equal(6, await _data.StockOfAsync(product.SkuIds[1]));
    }

    private sealed record CallerScope(NpgsqlConnection Connection, NpgsqlTransaction Transaction, DomainEventCollector Events)
        : IAsyncDisposable
    {
        public StockCallerTransaction AsCaller() => new(Transaction, Events);

        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    // Mô phỏng module `commerce`: tự mở kết nối và transaction, rồi truyền cho catalog.
    private async Task<CallerScope> BeginCallerAsync()
    {
        var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        var collector = new DomainEventCollector(_factory.Services.GetRequiredService<IEventDispatcher>());
        return new CallerScope(connection, transaction, collector);
    }

    private async Task<ReserveResult> ReserveInCallerAsync(CallerScope caller, string orderId, params StockItem[] items)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IStockService>();
        return await service.ReserveAsync(orderId, items, caller.AsCaller(), CancellationToken.None);
    }

    [Fact]
    // Bên gọi rollback thì tồn kho giữ nguyên, kể cả sau khi bên gọi flush collector. Sự kiện của thao tác bị hủy không được phát.
    public async Task Reserve_CallerRollsBack_StockUnchangedAndNoEvents()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5));
        var skuId = product.SkuIds[0];
        await using var caller = await BeginCallerAsync();

        var result = await ReserveInCallerAsync(caller, NewOrderId(), new StockItem(skuId, 5));
        Assert.True(result.Ok);
        await caller.Transaction.RollbackAsync();
        caller.Events.Discard();
        await caller.Events.FlushAsync(CancellationToken.None);

        Assert.Equal(5, await _data.StockOfAsync(skuId));
        Assert.Empty(StockEventsOf(skuId));
        Assert.Empty(await MovementsOfTypeAsync(skuId, InventoryMovementType.OrderReserve));
        // Giá trị tính sẵn của sản phẩm cũng hoàn nguyên cùng transaction của bên gọi.
        Assert.True((await _data.DisplayOfAsync(product.ProductId)).InStock);
    }

    [Fact]
    // Commit mà chưa flush thì chưa có sự kiện. Flush xong phát đúng một sự kiện cho mỗi SKU, với `Stock` và `PreviousStock` đúng.
    public async Task Reserve_CallerCommitsThenFlushes_EmitsOneEventPerSkuAfterFlush()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5), new StockTestData.SkuSeed(7));
        var first = product.SkuIds[0];
        var second = product.SkuIds[1];
        await using var caller = await BeginCallerAsync();

        var result = await ReserveInCallerAsync(caller, NewOrderId(), new StockItem(first, 2), new StockItem(second, 3));
        Assert.True(result.Ok);
        await caller.Transaction.CommitAsync();
        Assert.Empty(StockEventsOf(first, second));

        await caller.Events.FlushAsync(CancellationToken.None);

        Assert.Equal(3, await _data.StockOfAsync(first));
        Assert.Equal(4, await _data.StockOfAsync(second));
        var firstEvent = Assert.Single(StockEventsOf(first));
        Assert.Equal(new StockChangedEvent(product.ProductId, first, 3, 5, true, 1_000_000), firstEvent);
        var secondEvent = Assert.Single(StockEventsOf(second));
        Assert.Equal(new StockChangedEvent(product.ProductId, second, 4, 7, true, 1_000_000), secondEvent);
    }

    [Fact]
    // Thiếu hàng khi bên gọi sở hữu transaction: catalog không để lại thay đổi nào, nên bên gọi commit cũng không mất hàng.
    public async Task Reserve_ShortageInCallerTransaction_LeavesNoChangeEvenIfCallerCommits()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5), new StockTestData.SkuSeed(1));
        await using var caller = await BeginCallerAsync();

        var result = await ReserveInCallerAsync(caller, NewOrderId(),
            new StockItem(product.SkuIds[0], 2), new StockItem(product.SkuIds[1], 3));
        await caller.Transaction.CommitAsync();
        await caller.Events.FlushAsync(CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal(5, await _data.StockOfAsync(product.SkuIds[0]));
        Assert.Equal(1, await _data.StockOfAsync(product.SkuIds[1]));
        Assert.Empty(StockEventsOf(product.SkuIds.ToArray()));
    }

    [Fact]
    // Giữ hết hàng của SKU duy nhất thì `inStock` thành false trong cùng transaction. Trả hàng thì thành true lại.
    public async Task Reserve_LastUnits_FlipsInStockAndRelease_FlipsItBack()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(2, Price: 500_000));
        var orderId = NewOrderId();

        await ReserveAsync(orderId, new StockItem(product.SkuIds[0], 2));
        var afterReserve = await _data.DisplayOfAsync(product.ProductId);
        await ReleaseAsync(orderId);
        var afterRelease = await _data.DisplayOfAsync(product.ProductId);

        Assert.False(afterReserve.InStock);
        Assert.Equal(500_000, afterReserve.MinPrice);
        Assert.True(afterRelease.InStock);
    }

    [Fact]
    // Hết hàng ở SKU rẻ thì giá hiển thị chuyển sang SKU còn hàng.
    public async Task Reserve_CheapSkuSoldOut_MinPriceMovesToInStockSku()
    {
        var product = await _data.CreateProductAsync(
            new StockTestData.SkuSeed(1, Price: 100_000), new StockTestData.SkuSeed(5, Price: 200_000));

        await ReserveAsync(NewOrderId(), new StockItem(product.SkuIds[0], 1));

        var display = await _data.DisplayOfAsync(product.ProductId);
        Assert.Equal(200_000, display.MinPrice);
        Assert.True(display.InStock);
    }

    [Fact]
    // Đơn hàng cho hai SKU của cùng sản phẩm chạy đồng thời với đơn khác vẫn cho giá trị tính sẵn đúng.
    public async Task Reserve_ConcurrentOrdersOnSiblingSkus_KeepDisplayValuesConsistent()
    {
        var product = await _data.CreateProductAsync(
            new StockTestData.SkuSeed(1, Price: 100_000), new StockTestData.SkuSeed(1, Price: 200_000));

        await Task.WhenAll(
            Task.Run(() => ReserveAsync(NewOrderId(), new StockItem(product.SkuIds[0], 1))),
            Task.Run(() => ReserveAsync(NewOrderId(), new StockItem(product.SkuIds[1], 1))));

        var display = await _data.DisplayOfAsync(product.ProductId);
        Assert.False(display.InStock);
        Assert.Equal(100_000, display.MinPrice);
    }

    [Fact]
    // Số lượng không dương là lỗi lập trình của bên gọi, nên service ném lỗi thay vì âm thầm bỏ mục.
    public async Task Reserve_NonPositiveQuantity_Throws()
    {
        var product = await _data.CreateProductAsync(new StockTestData.SkuSeed(5));

        await Assert.ThrowsAsync<ArgumentException>(() => ReserveAsync(NewOrderId(), new StockItem(product.SkuIds[0], 0)));
        Assert.Equal(5, await _data.StockOfAsync(product.SkuIds[0]));
    }
}
