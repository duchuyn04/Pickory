using Pickory.Modules.Shipping.Ghn;

namespace Pickory.IntegrationTests.Shipping;

// Client GHN bọc client khác và chạy một hàm của test ngay sau khi GHN đã hủy đơn, trước khi bên gọi lưu DB.
// Test dùng để chèn webhook vào đúng khe giữa lời gọi GHN và bước lưu, nên kết quả không phụ thuộc lịch luồng.
internal sealed class CallbackAfterGhnClient(IGhnClient inner, Func<Task> afterCancel) : IGhnClient
{
    public async Task CancelOrderAsync(string orderCode, string reasonCode, string? reason, CancellationToken cancellationToken)
    {
        await inner.CancelOrderAsync(orderCode, reasonCode, reason, cancellationToken);
        await afterCancel();
    }

    public Task<IReadOnlyList<GhnProvince>> GetProvincesAsync(CancellationToken cancellationToken) =>
        inner.GetProvincesAsync(cancellationToken);

    public Task<IReadOnlyList<GhnWard>> GetWardsAsync(string provinceId, int offset, int limit, CancellationToken cancellationToken) =>
        inner.GetWardsAsync(provinceId, offset, limit, cancellationToken);

    public Task<GhnFee> CalculateFeeAsync(GhnFeeRequest request, CancellationToken cancellationToken) =>
        inner.CalculateFeeAsync(request, cancellationToken);

    public Task<GhnLeadTime> GetLeadTimeAsync(GhnLeadTimeRequest request, CancellationToken cancellationToken) =>
        inner.GetLeadTimeAsync(request, cancellationToken);

    public Task<GhnCreatedOrder> CreateOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken) =>
        inner.CreateOrderAsync(request, cancellationToken);

    public Task<GhnOrderDetail> GetOrderDetailAsync(string orderCode, CancellationToken cancellationToken) =>
        inner.GetOrderDetailAsync(orderCode, cancellationToken);
}
