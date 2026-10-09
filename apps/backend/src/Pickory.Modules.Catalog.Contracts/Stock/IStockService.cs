namespace Pickory.Modules.Catalog.Contracts.Stock;

// Giữ hàng và trả hàng cho module `commerce`. Cả hai hàm idempotent theo `orderId`.
public interface IStockService
{
    // Giữ hàng cho đơn theo kiểu tất cả hoặc không. Thiếu một SKU thì không SKU nào bị trừ.
    Task<ReserveResult> ReserveAsync(
        string orderId, IReadOnlyList<StockItem> items, StockCallerTransaction? caller, CancellationToken cancellationToken);

    // Trả lại hàng đã giữ cho đơn bị hủy hoặc quá hạn thanh toán. Đơn chưa giữ hàng thì không làm gì.
    Task ReleaseAsync(string orderId, StockCallerTransaction? caller, CancellationToken cancellationToken);
}
