namespace Pickory.Modules.Shipping.Ghn;

// Cổng gọi GHN, chỉ module shipping dùng nên là `internal`. Mọi lỗi từ GHN quy về hai lỗi trong Contracts.
internal interface IGhnClient
{
    Task<IReadOnlyList<GhnProvince>> GetProvincesAsync(CancellationToken cancellationToken);

    // Đọc một trang phường xã của tỉnh. Tỉnh không có hoặc hết trang thì trả danh sách rỗng.
    Task<IReadOnlyList<GhnWard>> GetWardsAsync(string provinceId, int offset, int limit, CancellationToken cancellationToken);

    Task<GhnFee> CalculateFeeAsync(GhnFeeRequest request, CancellationToken cancellationToken);

    // Thời gian giao dự kiến chỉ là thông tin phụ. Adapter không thử lại và dùng timeout ngắn hơn báo phí.
    Task<GhnLeadTime> GetLeadTimeAsync(GhnLeadTimeRequest request, CancellationToken cancellationToken);

    Task<GhnCreatedOrder> CreateOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken);

    // Hủy thành công thì trả bình thường. GHN báo đơn không hủy được thì ném `ShippingRejectedException`.
    Task CancelOrderAsync(string orderCode, string reasonCode, string? reason, CancellationToken cancellationToken);

    Task<GhnOrderDetail> GetOrderDetailAsync(string orderCode, CancellationToken cancellationToken);
}
