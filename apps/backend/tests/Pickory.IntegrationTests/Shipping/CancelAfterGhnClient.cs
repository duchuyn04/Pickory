using Pickory.Modules.Shipping.Ghn;

namespace Pickory.IntegrationTests.Shipping;

// Client GHN bọc client khác và hủy `CancellationTokenSource` ngay sau khi GHN đã tạo hoặc hủy đơn.
// Test dùng nó để mô phỏng bên gọi đóng request đúng lúc GHN đã đổi trạng thái.
internal sealed class CancelAfterGhnClient(IGhnClient inner, CancellationTokenSource callerCancellation) : IGhnClient
{
    public Task<GhnOrderPreview> PreviewOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken) =>
        inner.PreviewOrderAsync(request, cancellationToken);

    public async Task<GhnCreatedOrder> CreateOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken)
    {
        var created = await inner.CreateOrderAsync(request, cancellationToken);
        await callerCancellation.CancelAsync();
        return created;
    }

    public async Task CancelOrderAsync(string orderCode, string reasonCode, string? reason, CancellationToken cancellationToken)
    {
        await inner.CancelOrderAsync(orderCode, reasonCode, reason, cancellationToken);
        await callerCancellation.CancelAsync();
    }

    public Task<IReadOnlyList<GhnProvince>> GetProvincesAsync(CancellationToken cancellationToken) =>
        inner.GetProvincesAsync(cancellationToken);

    public Task<IReadOnlyList<GhnWard>> GetWardsAsync(string provinceId, int offset, int limit, CancellationToken cancellationToken) =>
        inner.GetWardsAsync(provinceId, offset, limit, cancellationToken);

    public Task<GhnFee> CalculateFeeAsync(GhnFeeRequest request, CancellationToken cancellationToken) =>
        inner.CalculateFeeAsync(request, cancellationToken);

    public Task<GhnLeadTime> GetLeadTimeAsync(GhnLeadTimeRequest request, CancellationToken cancellationToken) =>
        inner.GetLeadTimeAsync(request, cancellationToken);

    public Task<GhnOrderDetail> GetOrderDetailAsync(string orderCode, CancellationToken cancellationToken) =>
        inner.GetOrderDetailAsync(orderCode, cancellationToken);
}
