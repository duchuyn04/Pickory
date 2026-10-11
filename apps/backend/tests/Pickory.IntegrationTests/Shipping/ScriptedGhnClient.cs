using System.Globalization;
using Pickory.Modules.Shipping.Ghn;

namespace Pickory.IntegrationTests.Shipping;

// Client GHN cho test địa chỉ: test đặt sẵn tỉnh, phường, lỗi theo số thứ tự lời gọi, rồi đọc lại các lời gọi.
// `FakeGhnClient` không đủ vì dữ liệu cố định của nó không thử được phân trang và thứ tự sắp.
internal sealed class ScriptedGhnClient : IGhnClient
{
    private readonly Dictionary<string, List<GhnWard>> _wardsByProvinceCode = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Exception> _wardCallFailures = [];

    public List<GhnProvince> Provinces { get; } = [];

    // Lỗi ném ở lời gọi lấy tỉnh. Đặt `null` để lời gọi thành công.
    public Exception? ProvincesFailure { get; set; }

    public List<(string ProvinceCode, int Offset, int Limit)> WardCalls { get; } = [];

    // Bật thì mọi lời gọi lấy phường trả đủ `limit` dòng bất kể `offset`, giống GHN bỏ qua `offset`.
    public bool AlwaysReturnFullWardPage { get; set; }

    // Quá số lời gọi này thì ném lỗi. Test đỏ ngay thay vì treo cả bộ test khi code đang kiểm lặp vô hạn.
    private const int RunawayCallLimit = 500;

    public int ProvincesCallCount { get; private set; }

    public void AddProvince(long id, string name, int status = 1, IEnumerable<GhnWard>? wards = null)
    {
        Provinces.Add(new GhnProvince(id, name, status));
        _wardsByProvinceCode[id.ToString(CultureInfo.InvariantCulture)] = wards?.ToList() ?? [];
    }

    // Tạo `count` phường đang dùng cho một tỉnh, mã tăng dần từ `firstId`.
    public static List<GhnWard> CreateWards(long provinceId, long firstId, int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new GhnWard(firstId + index, $"Phường {firstId + index}", provinceId, 1))
            .ToList();
    }

    // Lời gọi lấy phường thứ `callNumber` (bắt đầu từ 1, tính qua mọi tỉnh) ném lỗi `failure`.
    public void FailWardCall(int callNumber, Exception failure)
    {
        _wardCallFailures[callNumber] = failure;
    }

    public Task<IReadOnlyList<GhnProvince>> GetProvincesAsync(CancellationToken cancellationToken)
    {
        ProvincesCallCount++;
        if (ProvincesFailure is not null)
        {
            throw ProvincesFailure;
        }

        return Task.FromResult<IReadOnlyList<GhnProvince>>(Provinces.ToList());
    }

    public Task<IReadOnlyList<GhnWard>> GetWardsAsync(string provinceId, int offset, int limit, CancellationToken cancellationToken)
    {
        WardCalls.Add((provinceId, offset, limit));
        if (WardCalls.Count > RunawayCallLimit)
        {
            throw new InvalidOperationException("Code đang kiểm gọi lấy phường/xã không dừng.");
        }

        if (AlwaysReturnFullWardPage)
        {
            var fullPage = CreateWards(long.Parse(provinceId, CultureInfo.InvariantCulture), 100000 + offset, limit);
            return Task.FromResult<IReadOnlyList<GhnWard>>(fullPage);
        }
        if (_wardCallFailures.TryGetValue(WardCalls.Count, out var failure))
        {
            throw failure;
        }

        var page = _wardsByProvinceCode.TryGetValue(provinceId, out var wards)
            ? wards.Skip(offset).Take(limit).ToList()
            : [];
        return Task.FromResult<IReadOnlyList<GhnWard>>(page);
    }

    public Task<GhnFee> CalculateFeeAsync(GhnFeeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GhnLeadTime> GetLeadTimeAsync(GhnLeadTimeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GhnCreatedOrder> CreateOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task CancelOrderAsync(string orderCode, string reasonCode, string? reason, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<GhnOrderDetail> GetOrderDetailAsync(string orderCode, CancellationToken cancellationToken) => throw new NotSupportedException();
}
