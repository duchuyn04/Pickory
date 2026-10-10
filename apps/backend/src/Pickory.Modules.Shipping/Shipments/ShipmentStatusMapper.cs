using Pickory.Modules.Shipping.Contracts.Shipments;

namespace Pickory.Modules.Shipping.Shipments;

// Ánh xạ trạng thái của GHN sang trạng thái chuẩn của Pickory theo bảng trong spec.
public static class ShipmentStatusMapper
{
    // Trạng thái gốc GHN dùng cho đơn đã hủy. Hủy tay, webhook và bản giả cùng dùng hằng này.
    public const string CancelledRawStatus = "cancel";

    // So khớp đúng chuỗi chữ thường của bảng spec. Chuỗi khác dạng viết có thể là trạng thái chưa được xác minh.
    private static readonly Dictionary<string, ShipmentStatus> StatusByGhnStatus = new(StringComparer.Ordinal)
    {
        ["ready_to_pick"] = ShipmentStatus.PendingPickup,
        ["picking"] = ShipmentStatus.PendingPickup,
        ["picked"] = ShipmentStatus.InTransit,
        ["storing"] = ShipmentStatus.InTransit,
        ["transporting"] = ShipmentStatus.InTransit,
        ["sorting"] = ShipmentStatus.InTransit,
        ["delivering"] = ShipmentStatus.Delivering,
        ["money_collect_delivering"] = ShipmentStatus.Delivering,
        ["delivered"] = ShipmentStatus.Delivered,
        ["delivery_fail"] = ShipmentStatus.DeliveryFailed,
        ["waiting_to_return"] = ShipmentStatus.Returning,
        ["return"] = ShipmentStatus.Returning,
        ["return_transporting"] = ShipmentStatus.Returning,
        ["return_sorting"] = ShipmentStatus.Returning,
        ["returning"] = ShipmentStatus.Returning,
        ["returned"] = ShipmentStatus.Returned,
        [CancelledRawStatus] = ShipmentStatus.Cancelled,
        ["exception"] = ShipmentStatus.Exception,
        ["damage"] = ShipmentStatus.Exception,
        ["lost"] = ShipmentStatus.Exception,
    };

    // Trả `null` khi không biết trạng thái. Bên gọi giữ trạng thái cũ và ghi log cảnh báo, luồng không bị hỏng.
    public static ShipmentStatus? Map(string? rawStatus)
    {
        // Chuỗi null hoặc rỗng không thể là khóa của từ điển. Trả `null` thay vì ném lỗi.
        if (string.IsNullOrEmpty(rawStatus))
        {
            return null;
        }

        return StatusByGhnStatus.TryGetValue(rawStatus, out var status) ? status : null;
    }
}
