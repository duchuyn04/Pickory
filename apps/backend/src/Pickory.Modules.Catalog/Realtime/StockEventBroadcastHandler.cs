using Pickory.Modules.Catalog.Contracts.Events;
using Pickory.Shared.Events;

namespace Pickory.Modules.Catalog.Realtime;

// Chuyển sự kiện tồn kho đã commit cho mọi kết nối SSE đang mở.
internal sealed class StockEventBroadcastHandler(StockEventHub eventHub) : IDomainEventHandler<StockChangedEvent>
{
    public Task HandleAsync(StockChangedEvent @event, CancellationToken cancellationToken = default)
    {
        // Số tồn mới và giá trị tính sẵn của sản phẩm được tính trong cùng transaction đổi kho.
        eventHub.Broadcast(new StockEvent(
            StockEventNames.StockChanged,
            new StockEventData(
                @event.ProductId, @event.SkuId, @event.Stock, @event.ProductInStock, @event.ProductMinPrice)));
        return Task.CompletedTask;
    }
}
