namespace Pickory.Modules.Catalog.Contracts.Stock;

// Một dòng của đơn cần giữ hàng. `Quantity` phải lớn hơn 0.
public sealed record StockItem(string SkuId, int Quantity);
