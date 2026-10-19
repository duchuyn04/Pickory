namespace Pickory.Modules.Commerce.Contracts.Orders;

// Nguồn của phí vận chuyển ghi trên đơn, với nhãn trong DB là `ghn`, `cached` và `fallback`.
// Chỉ nhân viên thấy giá trị này.
public enum ShippingFeeSource
{
    // GHN vừa tính phí cho đúng yêu cầu của đơn.
    Ghn,

    // GHN không phản hồi, nên đơn dùng phí GHN đã lưu cho cùng điều kiện và còn hạn.
    Cached,

    // GHN không phản hồi và không có phí đã lưu, nên đơn dùng bảng phí dự phòng của shop.
    Fallback,
}
