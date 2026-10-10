using System.Globalization;
using Pickory.Modules.Shipping.Contracts.Errors;
using Pickory.Modules.Shipping.Shipments;

namespace Pickory.Modules.Shipping.Ghn;

// Client GHN giả dùng khi thiếu `GHN_TOKEN`. Dữ liệu cố định, không gọi mạng, và chống tạo trùng theo `client_order_code` như GHN.
internal sealed class FakeGhnClient : IGhnClient
{
    // Mã đơn giả có dạng `FAKE000001`, tăng dần để test đoán được và đếm được số đơn đã tạo.
    private const string OrderCodePrefix = "FAKE";

    private static readonly GhnProvince[] Provinces =
    [
        new(1000001, "Hồ Chí Minh", 1),
        new(1000002, "Hà Nội", 1),
        // Đơn vị bị tắt, để job đồng bộ có dữ liệu thử nhánh "không còn dùng".
        new(1000099, "Tỉnh đã giải thể", 2),
    ];

    private static readonly GhnWard[] Wards =
    [
        new(1003646, "Phường Bến Nghé", 1000001, 1),
        new(1003647, "Phường Bến Thành", 1000001, 1),
        new(1003648, "Phường Sài Gòn", 1000001, 1),
        new(1003649, "Phường đã sáp nhập", 1000001, 2),
        new(1004001, "Phường Ba Đình", 1000002, 1),
        new(1004002, "Phường Hoàn Kiếm", 1000002, 1),
    ];

    // Thời điểm giao cố định của bản giả, để test đoán được ngày giao. Giá trị là 12:00 giờ Việt Nam ngày 15/10/2026.
    public static readonly long FixedLeadTimeUnixSeconds =
        new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(7)).ToUnixTimeSeconds();

    private readonly object _gate = new();
    private readonly Dictionary<FakeGhnOperation, FakeGhnFailure> _failures = [];
    private readonly Dictionary<string, FakeOrder> _ordersByCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _orderCodeByClientCode = new(StringComparer.Ordinal);
    private int _orderCounter;
    private int _orderDetailCallCount;

    // Bật lỗi cho một hàm cho tới khi gọi `ClearFailures`. SHIP-07 dùng `UnavailableAfterProcessing` để thử bấm tạo lại sau lỗi mạng.
    public void SetFailure(FakeGhnOperation operation, FakeGhnFailure failure)
    {
        lock (_gate)
        {
            _failures[operation] = failure;
        }
    }

    // Số đơn GHN giả đã tạo. Test dùng để chứng minh gọi lại cùng `client_order_code` không tạo đơn thứ hai.
    public int CreatedOrderCount
    {
        get
        {
            lock (_gate)
            {
                return _ordersByCode.Count;
            }
        }
    }

    // Số lần tra chi tiết đơn, kể cả lần bị đặt lỗi. Test dùng để chứng minh webhook và job không gọi GHN khi không cần.
    public int OrderDetailCallCount => Volatile.Read(ref _orderDetailCallCount);

    // Đặt trạng thái gốc của một đơn đã tạo, như GHN đổi trạng thái sau khi shipper lấy hàng. Mã đơn lạ thì ném lỗi để test không đặt nhầm.
    public void SetOrderStatus(string orderCode, string rawStatus)
    {
        lock (_gate)
        {
            if (!_ordersByCode.TryGetValue(orderCode, out var order))
            {
                throw new InvalidOperationException($"Đơn giả {orderCode} chưa được tạo.");
            }

            order.Status = rawStatus;
        }
    }

    public void ClearFailures()
    {
        lock (_gate)
        {
            _failures.Clear();
        }
    }

    public Task<IReadOnlyList<GhnProvince>> GetProvincesAsync(CancellationToken cancellationToken)
    {
        ThrowIfFailing(FakeGhnOperation.GetProvinces);
        return Task.FromResult<IReadOnlyList<GhnProvince>>(Provinces);
    }

    public Task<IReadOnlyList<GhnWard>> GetWardsAsync(string provinceId, int offset, int limit, CancellationToken cancellationToken)
    {
        ThrowIfFailing(FakeGhnOperation.GetWards);

        // Tỉnh không tồn tại thì trả danh sách rỗng, giống GHN trả `data: null`.
        var page = Wards
            .Where(ward => ward.ParentId.ToString(CultureInfo.InvariantCulture) == provinceId)
            .Skip(offset)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<GhnWard>>(page);
    }

    public Task<GhnFee> CalculateFeeAsync(GhnFeeRequest request, CancellationToken cancellationToken)
    {
        ThrowIfFailing(FakeGhnOperation.CalculateFee);
        return Task.FromResult(ComputeFee(request.Weight, request.InsuranceValue));
    }

    public Task<GhnLeadTime> GetLeadTimeAsync(GhnLeadTimeRequest request, CancellationToken cancellationToken)
    {
        ThrowIfFailing(FakeGhnOperation.GetLeadTime);
        return Task.FromResult(new GhnLeadTime(FixedLeadTimeUnixSeconds));
    }

    public Task<GhnCreatedOrder> CreateOrderAsync(GhnCreateOrderRequest request, CancellationToken cancellationToken)
    {
        var failure = FindFailure(FakeGhnOperation.CreateOrder);
        if (failure is FakeGhnFailure.Unavailable or FakeGhnFailure.Rejected)
        {
            throw CreateException(failure.Value);
        }

        GhnCreatedOrder created;
        lock (_gate)
        {
            // Gửi lại cùng `client_order_code` thì trả đơn đã tạo, kể cả đơn đã hủy, như GHN. Mã rỗng không dùng để chống trùng vì GHN coi trường này là tùy chọn.
            var hasClientCode = !string.IsNullOrWhiteSpace(request.ClientOrderCode);
            if (hasClientCode && _orderCodeByClientCode.TryGetValue(request.ClientOrderCode, out var existingCode))
            {
                created = ToCreatedOrder(_ordersByCode[existingCode]);
            }
            else
            {
                _orderCounter++;
                var orderCode = $"{OrderCodePrefix}{_orderCounter.ToString("D6", CultureInfo.InvariantCulture)}";
                var order = new FakeOrder(orderCode, request.ClientOrderCode, ComputeFee(request.Weight, request.InsuranceValue).Total);
                _ordersByCode[orderCode] = order;
                if (hasClientCode)
                {
                    _orderCodeByClientCode[request.ClientOrderCode] = orderCode;
                }

                created = ToCreatedOrder(order);
            }
        }

        // GHN đã tạo đơn nhưng phản hồi mất giữa đường: tác dụng phụ đã xảy ra rồi mới báo lỗi mạng.
        if (failure == FakeGhnFailure.UnavailableAfterProcessing)
        {
            throw new ShippingUnavailableException();
        }

        return Task.FromResult(created);
    }

    public Task CancelOrderAsync(string orderCode, string reasonCode, string? reason, CancellationToken cancellationToken)
    {
        var failure = FindFailure(FakeGhnOperation.CancelOrder);
        if (failure is FakeGhnFailure.Unavailable or FakeGhnFailure.Rejected)
        {
            throw CreateException(failure.Value);
        }

        lock (_gate)
        {
            // Mã đơn không có thì GHN trả HTTP 400, nên bản giả cũng từ chối.
            if (!_ordersByCode.TryGetValue(orderCode, out var order))
            {
                throw new ShippingRejectedException("Đơn hàng không tồn tại");
            }

            // Đơn chỉ hủy được khi chưa lấy hàng. GHN trả `result: false` cho đơn đã hủy, và bản giả từ chối tương ứng.
            if (order.Status != "ready_to_pick")
            {
                throw new ShippingRejectedException("Đơn không thể hủy ở trạng thái hiện tại");
            }

            order.Status = ShipmentStatusMapper.CancelledRawStatus;
        }

        if (failure == FakeGhnFailure.UnavailableAfterProcessing)
        {
            throw new ShippingUnavailableException();
        }

        return Task.CompletedTask;
    }

    public Task<GhnOrderDetail> GetOrderDetailAsync(string orderCode, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _orderDetailCallCount);
        ThrowIfFailing(FakeGhnOperation.GetOrderDetail);

        lock (_gate)
        {
            if (!_ordersByCode.TryGetValue(orderCode, out var order))
            {
                throw new ShippingRejectedException("Đơn hàng không tồn tại");
            }

            return Task.FromResult(new GhnOrderDetail(order.OrderCode, order.ClientOrderCode, order.Status, UpdatedDate: null));
        }
    }

    // Công thức cố định: 20.000 đồng, cộng 2.000 đồng cho mỗi 500 g bắt đầu, cộng bảo hiểm 0,5% giá trị khai.
    private static GhnFee ComputeFee(int weightGrams, int insuranceValue)
    {
        var halfKilogramBlocks = (weightGrams + 499) / 500;
        var serviceFee = 20000 + (2000 * halfKilogramBlocks);
        var insuranceFee = insuranceValue / 200;
        return new GhnFee(serviceFee + insuranceFee, serviceFee, insuranceFee, CodFee: 0);
    }

    private static GhnCreatedOrder ToCreatedOrder(FakeOrder order) => new(order.OrderCode, order.TotalFee, ExpectedDeliveryTime: null);

    // Với các hàm không có tác dụng phụ, mọi kiểu lỗi đều ném ngay.
    private void ThrowIfFailing(FakeGhnOperation operation)
    {
        var failure = FindFailure(operation);
        if (failure is not null)
        {
            throw CreateException(failure.Value);
        }
    }

    private FakeGhnFailure? FindFailure(FakeGhnOperation operation)
    {
        lock (_gate)
        {
            return _failures.TryGetValue(operation, out var failure) ? failure : null;
        }
    }

    private static Exception CreateException(FakeGhnFailure failure) => failure == FakeGhnFailure.Rejected
        ? new ShippingRejectedException("Client giả được đặt chế độ từ chối.")
        : new ShippingUnavailableException();

    private sealed class FakeOrder(string orderCode, string clientOrderCode, int totalFee)
    {
        public string OrderCode { get; } = orderCode;
        public string ClientOrderCode { get; } = clientOrderCode;
        public int TotalFee { get; } = totalFee;
        public string Status { get; set; } = "ready_to_pick";
    }
}
