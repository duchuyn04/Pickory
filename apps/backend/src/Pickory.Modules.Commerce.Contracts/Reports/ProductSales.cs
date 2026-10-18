namespace Pickory.Modules.Commerce.Contracts.Reports;

// Doanh thu và số lượng bán của một sản phẩm. Doanh thu là tổng `UnitPrice * Quantity` và không gồm phí ship.
public sealed record ProductSales(string ProductId, long Revenue, int Quantity);
