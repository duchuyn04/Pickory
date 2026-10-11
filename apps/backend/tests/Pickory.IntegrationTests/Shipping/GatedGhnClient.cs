using Pickory.Modules.Shipping.Ghn;

namespace Pickory.IntegrationTests.Shipping;

// Client GHN giữ các lời gọi tạo đơn cho tới khi đủ số bên gọi cùng tới cổng.
// Mọi bên đã qua bước kiểm `ref` trước khi bên nào chèn vận đơn, nên test không phụ thuộc may rủi về thời gian.
internal sealed class GatedGhnClient(IGhnClient inner, int participants) : IGhnClient
{
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public async Task<GhnCreatedOrder> CreateOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrived) == participants)
        {
            _allArrived.SetResult();
        }

        // Giới hạn chờ để test đỏ rõ ràng thay vì treo khi có bên gọi không bao giờ tới.
        await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        return await inner.CreateOrderAsync(request, cancellationToken);
    }

    public Task<IReadOnlyList<GhnProvince>> GetProvincesAsync(CancellationToken cancellationToken) =>
        inner.GetProvincesAsync(cancellationToken);

    public Task<IReadOnlyList<GhnWard>> GetWardsAsync(string provinceId, int offset, int limit, CancellationToken cancellationToken) =>
        inner.GetWardsAsync(provinceId, offset, limit, cancellationToken);

    public Task<GhnFee> CalculateFeeAsync(GhnFeeRequest request, CancellationToken cancellationToken) =>
        inner.CalculateFeeAsync(request, cancellationToken);

    public Task<GhnLeadTime> GetLeadTimeAsync(GhnLeadTimeRequest request, CancellationToken cancellationToken) =>
        inner.GetLeadTimeAsync(request, cancellationToken);

    public Task CancelOrderAsync(string orderCode, string reasonCode, string? reason, CancellationToken cancellationToken) =>
        inner.CancelOrderAsync(orderCode, reasonCode, reason, cancellationToken);

    public Task<GhnOrderDetail> GetOrderDetailAsync(string orderCode, CancellationToken cancellationToken) =>
        inner.GetOrderDetailAsync(orderCode, cancellationToken);
}
