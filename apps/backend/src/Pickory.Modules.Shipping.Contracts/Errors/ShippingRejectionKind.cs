namespace Pickory.Modules.Shipping.Contracts.Errors;

// Loại lý do đơn vị vận chuyển từ chối, để bên gọi chọn cách phản hồi mà không đọc chữ trong thông báo.
public enum ShippingRejectionKind
{
    // Mọi lý do khác. Thông báo giữ nguyên lời của đơn vị vận chuyển.
    Other,

    // Tên đường không khớp với phường/xã của địa chỉ nhận (GHN báo `To address conflict`).
    ToAddressMismatch,

    // GHN báo `From address conflict`. GHN từng gắn nhãn này cho cả lỗi của địa chỉ nhận, nên chưa chắc lỗi nằm ở địa chỉ lấy hàng.
    FromAddressMismatch,

    // Tỉnh/thành hoặc phường/xã của địa chỉ lấy hàng trong cấu hình không còn trong danh mục của đơn vị vận chuyển.
    PickupPlaceNotInCatalog,
}
