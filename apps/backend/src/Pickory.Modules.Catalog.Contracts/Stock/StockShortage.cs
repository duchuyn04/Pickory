namespace Pickory.Modules.Catalog.Contracts.Stock;

// SKU không đủ hàng cho đơn. `Available` là số còn lại tại lúc kiểm tra.
public sealed record StockShortage(string SkuId, int Requested, int Available);
