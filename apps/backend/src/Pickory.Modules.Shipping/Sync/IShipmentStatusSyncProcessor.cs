namespace Pickory.Modules.Shipping.Sync;

// Một vòng tra lại trạng thái các vận đơn chưa kết thúc. Worker nền gọi cổng này, và test gọi thẳng để không chờ đồng hồ.
internal interface IShipmentStatusSyncProcessor
{
    // Lỗi của từng vận đơn được ghi log và đếm vào kết quả. Chỉ lỗi dùng chung như mất DB mới ném ra cho bên gọi.
    Task<ShipmentStatusSyncResult> SyncAsync(CancellationToken cancellationToken);
}
