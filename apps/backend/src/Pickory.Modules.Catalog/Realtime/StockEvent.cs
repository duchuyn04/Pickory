namespace Pickory.Modules.Catalog.Realtime;

// Tên sự kiện SSE. Client chọn bộ nhận theo tên này.
public static class StockEventNames
{
    public const string Connected = "connected";
    public const string StockChanged = "stock.changed";
    public const string Heartbeat = "heartbeat";
}

// Một sự kiện chờ gửi cho các kết nối SSE.
internal sealed record StockEvent(string Name, object Data);

// Dữ liệu công khai của một lần đổi tồn kho. Không có số tồn trước đó.
// `MinPrice` và `ProductInStock` là giá trị tính sẵn sau lần đổi, để danh sách sản phẩm cập nhật đúng.
public sealed record StockEventData(
    string ProductId,
    string SkuId,
    int Stock,
    bool ProductInStock,
    int? MinPrice);

// Nhịp tim giữ kết nối sống qua proxy. Client bỏ qua dữ liệu này.
public sealed record StockHeartbeatData(DateTime At);
