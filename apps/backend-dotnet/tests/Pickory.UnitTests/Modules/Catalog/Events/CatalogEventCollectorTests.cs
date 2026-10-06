using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Modules.Catalog.Events;
using Pickory.Shared.Events;
using Xunit;

namespace Pickory.UnitTests.Modules.Catalog.Events;

// Port phần `createEventCollector` của `events.test.ts` ở bản Node (CAT-03).
public sealed class CatalogEventCollectorTests
{
    [Fact]
    // Sự kiện chỉ phát khi gọi `FlushAsync`. Gọi lần hai không phát lại.
    public async Task FlushAsync_PublishesQueuedEventsOnce()
    {
        var dispatcher = new RecordingDispatcher();
        var collector = new CatalogEventCollector(dispatcher);

        collector.Emit(new ProductChangedEvent("prod-collector", ProductChange.Created));
        Assert.Empty(dispatcher.Published);
        await collector.FlushAsync(CancellationToken.None);
        await collector.FlushAsync(CancellationToken.None);

        Assert.Equal([new ProductChangedEvent("prod-collector", ProductChange.Created)], dispatcher.Published);
    }

    [Fact]
    // Bỏ hàng đợi khi transaction bị hủy. Lần flush sau không phát sự kiện đã bỏ.
    public async Task Discard_DropsQueuedEvents()
    {
        var dispatcher = new RecordingDispatcher();
        var collector = new CatalogEventCollector(dispatcher);
        collector.Emit(new StockChangedEvent("sku-1", 0, 5));

        collector.Discard();
        await collector.FlushAsync(CancellationToken.None);

        Assert.Empty(dispatcher.Published);
    }

    [Fact]
    // Sự kiện phát theo đúng thứ tự đã gom.
    public async Task FlushAsync_KeepsOrder()
    {
        var dispatcher = new RecordingDispatcher();
        var collector = new CatalogEventCollector(dispatcher);
        collector.Emit(new ProductChangedEvent("prod-1", ProductChange.Created));
        collector.Emit(new StockChangedEvent("sku-1", 10, 0));

        await collector.FlushAsync(CancellationToken.None);

        Assert.IsType<ProductChangedEvent>(dispatcher.Published[0]);
        Assert.IsType<StockChangedEvent>(dispatcher.Published[1]);
    }

    // Dispatcher giả ghi lại sự kiện đã phát theo thứ tự.
    private sealed class RecordingDispatcher : IEventDispatcher
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}
