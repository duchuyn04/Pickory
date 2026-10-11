namespace Pickory.Modules.Shipping.Shipments;

// Kết quả của một lần áp trạng thái GHN lên vận đơn. Bên gọi đếm kết quả để ghi log và để test kiểm.
internal enum ShipmentStatusUpdateOutcome
{
    // Trạng thái gốc bằng trạng thái đã lưu, hoặc vận đơn đã kết thúc. DB không đổi.
    NoChange,

    // Trạng thái gốc của GHN chưa có trong bảng ánh xạ. DB không đổi.
    UnknownStatus,

    // Trạng thái gốc đổi nhưng trạng thái chuẩn không đổi. Ghi lịch sử, không phát sự kiện.
    RawStatusUpdated,

    // Trạng thái chuẩn đổi. Ghi lịch sử và phát sự kiện.
    StatusChanged,

    // Bên khác đã ghi trước. Bên này không ghi lịch sử và không phát sự kiện.
    LostRace,
}
