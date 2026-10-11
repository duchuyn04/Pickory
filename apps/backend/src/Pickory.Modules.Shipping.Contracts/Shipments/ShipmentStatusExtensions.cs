namespace Pickory.Modules.Shipping.Contracts.Shipments;

public static class ShipmentStatusExtensions
{
    // Trạng thái kết thúc là trạng thái job đồng bộ dự phòng không cần tra lại nữa.
    public static bool IsTerminal(this ShipmentStatus status)
    {
        // Liệt kê tường minh để trạng thái mới thêm vào enum mặc định là chưa kết thúc.
        return status is ShipmentStatus.Delivered or ShipmentStatus.Returned or ShipmentStatus.Cancelled;
    }
}
