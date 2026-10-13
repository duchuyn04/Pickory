namespace Pickory.Modules.Shipping.Contracts.Events;

// Bên gây ra thay đổi trạng thái vận đơn. Bên nhận dùng giá trị này để phân biệt thao tác của shop với tin từ hãng vận chuyển.
public enum ShipmentStatusChangeSource
{
    // Nhân viên shop thao tác qua API (tạo hoặc hủy vận đơn).
    Shop,

    // Hãng vận chuyển báo về qua webhook hoặc job đồng bộ.
    Carrier,
}
