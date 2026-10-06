using Pickory.Shared.Events;

namespace Pickory.Modules.Catalog.Contracts.Events;

// Phát sau khi tồn kho của một SKU đã commit. `PreviousStock` cho biết SKU vừa hết hàng hay vừa có hàng lại.
public sealed record StockChangedEvent(string SkuId, int Stock, int PreviousStock) : IDomainEvent;
