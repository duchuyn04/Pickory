using Pickory.Modules.Shipping.Ghn;

namespace Pickory.IntegrationTests.Shipping;

// Bọc một client GHN để test đếm lời gọi và đọc lại request đã gửi. Kết quả và chế độ lỗi vẫn do client bên trong quyết định.
internal sealed class RecordingGhnClient(IGhnClient inner) : IGhnClient
{
    // Client bên trong. Test dùng để đặt chế độ lỗi và đếm đơn của `FakeGhnClient`.
    public IGhnClient Inner => inner;

    public List<GhnFeeRequest> FeeRequests { get; } = [];

    public List<GhnLeadTimeRequest> LeadTimeRequests { get; } = [];

    public List<GhnCreateOrderRequest> CreateOrderRequests { get; } = [];

    public List<string> CancelledOrderCodes { get; } = [];

    public List<string> CancelReasonCodes { get; } = [];

    public int TotalCallCount { get; private set; }

    public Task<IReadOnlyList<GhnProvince>> GetProvincesAsync(CancellationToken cancellationToken)
    {
        TotalCallCount++;
        return inner.GetProvincesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<GhnWard>> GetWardsAsync(string provinceId, int offset, int limit, CancellationToken cancellationToken)
    {
        TotalCallCount++;
        return inner.GetWardsAsync(provinceId, offset, limit, cancellationToken);
    }

    public Task<GhnFee> CalculateFeeAsync(GhnFeeRequest request, CancellationToken cancellationToken)
    {
        TotalCallCount++;
        FeeRequests.Add(request);
        return inner.CalculateFeeAsync(request, cancellationToken);
    }

    public Task<GhnLeadTime> GetLeadTimeAsync(GhnLeadTimeRequest request, CancellationToken cancellationToken)
    {
        TotalCallCount++;
        LeadTimeRequests.Add(request);
        return inner.GetLeadTimeAsync(request, cancellationToken);
    }

    // Chạy ngay sau khi GHN tạo đơn xong, trước khi bên gọi lưu DB. Test dùng để chèn thao tác khác đúng vào khe này.
    public Func<Task>? AfterCreateOrder { get; set; }

    public async Task<GhnCreatedOrder> CreateOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken)
    {
        TotalCallCount++;
        CreateOrderRequests.Add(request);
        var created = await inner.CreateOrderAsync(request, cancellationToken);
        if (AfterCreateOrder is { } afterCreateOrder)
        {
            await afterCreateOrder();
        }

        return created;
    }

    public Task CancelOrderAsync(string orderCode, string reasonCode, string? reason, CancellationToken cancellationToken)
    {
        TotalCallCount++;
        CancelledOrderCodes.Add(orderCode);
        CancelReasonCodes.Add(reasonCode);
        return inner.CancelOrderAsync(orderCode, reasonCode, reason, cancellationToken);
    }

    public Task<GhnOrderDetail> GetOrderDetailAsync(string orderCode, CancellationToken cancellationToken)
    {
        TotalCallCount++;
        return inner.GetOrderDetailAsync(orderCode, cancellationToken);
    }
}
