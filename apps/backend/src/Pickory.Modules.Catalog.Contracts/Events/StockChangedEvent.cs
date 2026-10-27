using Pickory.Shared.Events;

namespace Pickory.Modules.Catalog.Contracts.Events;

// Phát sau khi tồn kho của một SKU đã commit. `PreviousStock` cho biết SKU vừa hết hàng hay vừa có hàng lại.
// `ProductInStock` và `ProductMinPrice` là giá trị tính sẵn của sản phẩm sau lần đổi này.
public sealed record StockChangedEvent(
    string ProductId,
    string SkuId,
    int Stock,
    int PreviousStock,
    bool ProductInStock,
    int? ProductMinPrice) : IDomainEvent;
