namespace Pickory.Modules.Shipping.Ghn;

// Tám hàm của `IGhnClient`, để test chọn hàm nào của client giả bị lỗi.
internal enum FakeGhnOperation
{
    GetProvinces,
    GetWards,
    CalculateFee,
    GetLeadTime,
    CreateOrder,
    PreviewOrder,
    CancelOrder,
    GetOrderDetail,
}

// Kiểu lỗi mà client giả mô phỏng.
internal enum FakeGhnFailure
{
    // Lỗi mạng hoặc GHN lỗi: ném `ShippingUnavailableException`, chưa có tác dụng phụ nào.
    Unavailable,

    // GHN từ chối yêu cầu: ném `ShippingRejectedException`.
    Rejected,

    // GHN đã tạo hoặc hủy đơn xong nhưng phản hồi mất giữa đường. Client giả đổi trạng thái đơn rồi mới ném `ShippingUnavailableException`.
    UnavailableAfterProcessing,
}
