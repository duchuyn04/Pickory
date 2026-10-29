using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Contracts.Reports;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Reports;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Reports;

// Gom dòng đơn và dòng thanh toán thành số liệu từng ngày giờ Việt Nam. Lớp gom không chạm DB.
public sealed class DailySalesAggregatorTests
{
    private static readonly DateRange ThreeDays = new(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7));

    // Giữa ngày 5/10 giờ Việt Nam (05:00 UTC cùng ngày) và giữa ngày 6/10 (05:00 UTC).
    private static readonly DateTime MiddayOct5 = new(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MiddayOct6 = new(2026, 10, 6, 5, 0, 0, DateTimeKind.Utc);

    private static CreatedOrderRow Created(
        DateTime createdAtUtc,
        OrderStatus status = OrderStatus.PendingConfirmation,
        PaymentMethod method = PaymentMethod.Cod,
        int total = 100_000,
        int shippingDiscount = 0,
        bool expiredFlag = false,
        string? cancelReason = null) =>
        new(createdAtUtc, status, method, total, shippingDiscount, expiredFlag, cancelReason);

    private static OrderPaymentRow Payment(
        DateTime paidAtUtc,
        int total = 100_000,
        PaymentStatus status = PaymentStatus.Paid,
        int soldQuantity = 0,
        PaymentMethod method = PaymentMethod.Cod) =>
        new("order-" + Guid.NewGuid().ToString("N"), paidAtUtc, total, status, method, soldQuantity);

    private static DailySales DayOf(IReadOnlyList<DailySales> days, int dayOfMonth) =>
        days.Single(day => day.Day == new DateOnly(2026, 10, dayOfMonth));

    [Fact]
    // Khoảng ba ngày không có dữ liệu vẫn trả đủ ba dòng tăng dần, mọi số bằng 0.
    public void Build_WithoutRows_ReturnsOneZeroRowPerDayInAscendingOrder()
    {
        var days = DailySalesAggregator.Build(ThreeDays, [], []);

        Assert.Equal(
            [new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 7)],
            days.Select(day => day.Day));
        Assert.All(days, day =>
        {
            Assert.Equal(0, day.RecognizedRevenue);
            Assert.Equal(0, day.PaidOrderCount);
            Assert.Equal(0, day.PlacedOrderValue);
            Assert.Equal(0, day.CreatedOrderCount);
            Assert.Equal(new CancellationCounts(0, 0, 0), day.Cancellations);
            Assert.Equal(new PaymentMethodCounts(0, 0), day.PaymentMethods);
            Assert.Equal(new OrderStatusCounts(0, 0, 0, 0, 0, 0, 0), day.OrderStatuses);
            Assert.Equal(0, day.ShippingDiscount);
        });
    }

    [Fact]
    // Khoảng một ngày (From bằng To) trả đúng một dòng.
    public void Build_SingleDayRange_ReturnsOneRow()
    {
        var day = new DateOnly(2026, 10, 5);

        var days = DailySalesAggregator.Build(new DateRange(day, day), [], []);

        Assert.Equal(day, Assert.Single(days).Day);
    }

    [Fact]
    // Chỉ đơn `Paid` vào doanh thu. `RefundPending` và `Refunded` là tiền sẽ trả hoặc đã trả lại khách.
    public void Build_RecognizedRevenue_IgnoresRefundPendingAndRefundedOrders()
    {
        OrderPaymentRow[] payments =
        [
            Payment(MiddayOct5, total: 100_000, PaymentStatus.Paid),
            Payment(MiddayOct5, total: 20_000, PaymentStatus.RefundPending),
            Payment(MiddayOct5, total: 3_000, PaymentStatus.Refunded),
            Payment(MiddayOct5, total: 400_000, PaymentStatus.Unpaid),
        ];

        var day = DayOf(DailySalesAggregator.Build(ThreeDays, payments, []), 5);

        Assert.Equal(100_000, day.RecognizedRevenue);
        Assert.Equal(1, day.PaidOrderCount);
    }

    [Fact]
    // Số lượng sản phẩm bán ra chỉ cộng từ đơn có doanh thu ghi nhận.
    public void Build_PaymentWithSoldQuantity_AggregatesOnlyForRecognizedRevenue()
    {
        var payments = new[]
        {
            Payment(MiddayOct5, total: 100_000, status: PaymentStatus.Paid, soldQuantity: 3),
            Payment(MiddayOct5, total: 50_000, status: PaymentStatus.RefundPending, soldQuantity: 2),
            Payment(MiddayOct6, total: 200_000, status: PaymentStatus.Paid, soldQuantity: 5),
        };

        var oct5 = DayOf(DailySalesAggregator.Build(ThreeDays, payments, []), 5);
        var oct6 = DayOf(DailySalesAggregator.Build(ThreeDays, payments, []), 6);

        Assert.Equal(3, oct5.SoldQuantity);
        Assert.Equal(5, oct6.SoldQuantity);
    }

    [Fact]
    // Doanh thu xếp theo ngày thanh toán, còn giá trị đơn đặt xếp theo ngày tạo đơn.
    public void Build_OrderCreatedOnDayOnePaidOnDayTwo_SplitsRevenueAndPlacedValueAcrossDays()
    {
        var days = DailySalesAggregator.Build(
            ThreeDays,
            [Payment(MiddayOct6, total: 250_000)],
            [Created(MiddayOct5, total: 250_000)]);

        Assert.Equal(0, DayOf(days, 5).RecognizedRevenue);
        Assert.Equal(250_000, DayOf(days, 5).PlacedOrderValue);
        Assert.Equal(250_000, DayOf(days, 6).RecognizedRevenue);
        Assert.Equal(1, DayOf(days, 6).PaidOrderCount);
        Assert.Equal(0, DayOf(days, 6).PlacedOrderValue);
    }

    [Fact]
    // Đơn hết hạn không vào giá trị đơn đặt và giảm phí ship. Đơn chưa thanh toán không vào bộ đếm phương thức.
    public void Build_PaymentExpiredOrder_IsCreatedAndCancelledButNotPlaced()
    {
        CreatedOrderRow[] orders =
        [
            Created(MiddayOct5, OrderStatus.Cancelled, PaymentMethod.Sepay, total: 500_000, shippingDiscount: 10_000, expiredFlag: true),
            Created(MiddayOct5, OrderStatus.PendingConfirmation, PaymentMethod.Sepay, total: 200_000, shippingDiscount: 5_000),
        ];

        var day = DayOf(DailySalesAggregator.Build(ThreeDays, [], orders), 5);

        Assert.Equal(2, day.CreatedOrderCount);
        Assert.Equal(200_000, day.PlacedOrderValue);
        Assert.Equal(5_000, day.ShippingDiscount);
        Assert.Equal(new PaymentMethodCounts(Cod: 0, Sepay: 0), day.PaymentMethods);
        Assert.Equal(new CancellationCounts(ByCustomer: 0, ByShop: 0, PaymentExpired: 1), day.Cancellations);
    }

    [Fact]
    // Đơn do khách hoặc shop hủy vẫn nằm trong giá trị đơn đặt. Chỉ đơn hết hạn bị loại.
    public void Build_CustomerAndShopCancellations_StayInPlacedValueAndSplitByReason()
    {
        CreatedOrderRow[] orders =
        [
            Created(MiddayOct5, OrderStatus.Cancelled, total: 100_000, cancelReason: OrderReportMarkers.CustomerCancellationReason),
            Created(MiddayOct5, OrderStatus.Cancelled, total: 30_000, cancelReason: OrderReportMarkers.CustomerCancellationReason),
            Created(MiddayOct5, OrderStatus.Cancelled, total: 7_000, cancelReason: "Hết hàng."),
            Created(MiddayOct5, OrderStatus.Cancelled, total: 1_000, cancelReason: null),
        ];

        var day = DayOf(DailySalesAggregator.Build(ThreeDays, [], orders), 5);

        Assert.Equal(138_000, day.PlacedOrderValue);
        Assert.Equal(new CancellationCounts(ByCustomer: 2, ByShop: 2, PaymentExpired: 0), day.Cancellations);
    }

    [Fact]
    // Đơn khôi phục sau khi khách trả muộn đã bỏ cờ hết hạn và không còn `Cancelled`, nên vào giá trị đơn đặt.
    public void Build_RestoredLatePaymentOrder_CountsInPlacedValue()
    {
        var day = DayOf(
            DailySalesAggregator.Build(
                ThreeDays, [], [Created(MiddayOct5, OrderStatus.PendingConfirmation, PaymentMethod.Sepay, total: 90_000, expiredFlag: false)]),
            5);

        Assert.Equal(90_000, day.PlacedOrderValue);
        Assert.Equal(new CancellationCounts(0, 0, 0), day.Cancellations);
    }

    [Fact]
    // Lý do hủy chỉ đếm cho đơn `Cancelled`. Đơn đang chạy mang lý do cũ trong lịch sử không bị đếm.
    public void Build_NonCancelledOrder_IsNeverCountedAsCancellation()
    {
        var day = DayOf(
            DailySalesAggregator.Build(
                ThreeDays, [], [Created(MiddayOct5, OrderStatus.Confirmed, cancelReason: OrderReportMarkers.CustomerCancellationReason)]),
            5);

        Assert.Equal(new CancellationCounts(0, 0, 0), day.Cancellations);
    }

    [Fact]
    // Biên nửa đêm giờ Việt Nam: 16:59:59 UTC thuộc ngày D, 17:00:00 UTC thuộc ngày D+1. Áp dụng cho cả ngày tạo và ngày thanh toán.
    public void Build_VietnamMidnightBoundary_SplitsCreatedAndPaidRowsAcrossDays()
    {
        var lastSecondOct5 = new DateTime(2026, 10, 5, 16, 59, 59, DateTimeKind.Utc);
        var midnightOct6 = new DateTime(2026, 10, 5, 17, 0, 0, DateTimeKind.Utc);

        var days = DailySalesAggregator.Build(
            ThreeDays,
            [Payment(lastSecondOct5, total: 1_000), Payment(midnightOct6, total: 20_000)],
            [Created(lastSecondOct5, total: 300_000), Created(midnightOct6, total: 4_000_000)]);

        Assert.Equal(1_000, DayOf(days, 5).RecognizedRevenue);
        Assert.Equal(300_000, DayOf(days, 5).PlacedOrderValue);
        Assert.Equal(20_000, DayOf(days, 6).RecognizedRevenue);
        Assert.Equal(4_000_000, DayOf(days, 6).PlacedOrderValue);
    }

    [Fact]
    // Tổng hai đơn `int` có thể vượt `int.MaxValue`. Cộng bằng `long` nên không tràn và không ném lỗi.
    public void Build_SumsMoneyAsLong_WithoutOverflow()
    {
        var day = DayOf(
            DailySalesAggregator.Build(
                ThreeDays,
                [Payment(MiddayOct5, int.MaxValue), Payment(MiddayOct5, int.MaxValue)],
                [Created(MiddayOct5, total: int.MaxValue), Created(MiddayOct5, total: int.MaxValue)]),
            5);

        Assert.Equal(2L * int.MaxValue, day.RecognizedRevenue);
        Assert.Equal(2L * int.MaxValue, day.PlacedOrderValue);
    }

    [Fact]
    // Dòng nằm ngoài khoảng bị bỏ qua. Bộ lọc SQL đã chặn các dòng này, nên đây chỉ là lớp bảo vệ thứ hai.
    public void Build_RowsOutsideRange_AreIgnored()
    {
        var beforeRange = new DateTime(2026, 10, 4, 5, 0, 0, DateTimeKind.Utc);
        var afterRange = new DateTime(2026, 10, 8, 5, 0, 0, DateTimeKind.Utc);

        var days = DailySalesAggregator.Build(
            ThreeDays,
            [Payment(beforeRange), Payment(afterRange)],
            [Created(beforeRange), Created(afterRange)]);

        Assert.Equal(3, days.Count);
        Assert.All(days, day => Assert.Equal(0, day.CreatedOrderCount + day.PaidOrderCount));
    }

    [Fact]
    // Mỗi trạng thái hiện tại có một bộ đếm riêng để báo cáo phản ánh đúng các đơn tạo trong kỳ.
    public void Build_CurrentOrderStatuses_CountsEveryStatus()
    {
        CreatedOrderRow[] orders = Enum.GetValues<OrderStatus>()
            .Select(status => Created(MiddayOct5, status))
            .ToArray();

        var day = DayOf(DailySalesAggregator.Build(ThreeDays, [], orders), 5);

        Assert.Equal(1, day.OrderStatuses.PendingPayment);
        Assert.Equal(1, day.OrderStatuses.PendingConfirmation);
        Assert.Equal(1, day.OrderStatuses.Confirmed);
        Assert.Equal(1, day.OrderStatuses.Shipping);
        Assert.Equal(1, day.OrderStatuses.Delivered);
        Assert.Equal(1, day.OrderStatuses.Returned);
        Assert.Equal(1, day.OrderStatuses.Cancelled);
    }

    [Fact]
    // Phương thức thanh toán chưa có bộ đếm phải ném lỗi, để không bị đếm nhầm thành COD một cách im lặng.
    public void Build_UnknownPaymentMethod_Throws()
    {
        OrderPaymentRow[] payments = [Payment(MiddayOct5, method: (PaymentMethod)99)];

        Assert.Throws<InvalidOperationException>(() => DailySalesAggregator.Build(ThreeDays, payments, []));
    }

    [Fact]
    // Mọi giá trị của `PaymentMethod` phải có bộ đếm. Thêm giá trị mới vào enum mà quên bộ đếm sẽ làm test đỏ.
    public void Build_EveryPaymentMethod_HasCounter()
    {
        Assert.All(Enum.GetValues<PaymentMethod>(), method =>
        {
            OrderPaymentRow[] payments = [Payment(MiddayOct5, method: method)];

            var day = DayOf(DailySalesAggregator.Build(ThreeDays, payments, []), 5);

            Assert.Equal(1, day.PaymentMethods.Sepay + day.PaymentMethods.Cod);
        });
    }

    [Fact]
    // Đếm phương thức từ dòng thanh toán và cộng giảm phí ship từ các đơn đặt.
    public void Build_CountsPaymentMethodsAndShippingDiscount()
    {
        OrderPaymentRow[] payments =
        [
            Payment(MiddayOct5, method: PaymentMethod.Cod),
            Payment(MiddayOct5, method: PaymentMethod.Sepay),
            Payment(MiddayOct5, method: PaymentMethod.Sepay),
        ];
        CreatedOrderRow[] orders =
        [
            Created(MiddayOct5, method: PaymentMethod.Cod, shippingDiscount: 10_000),
            Created(MiddayOct5, method: PaymentMethod.Sepay, shippingDiscount: 5_000),
            Created(MiddayOct5, method: PaymentMethod.Sepay, shippingDiscount: 0),
        ];

        var day = DayOf(DailySalesAggregator.Build(ThreeDays, payments, orders), 5);

        Assert.Equal(new PaymentMethodCounts(Cod: 1, Sepay: 2), day.PaymentMethods);
        Assert.Equal(15_000, day.ShippingDiscount);
        Assert.Equal(3, day.CreatedOrderCount);
    }

    [Fact]
    // Phương thức chỉ được đếm vào ngày thanh toán. Ngày tạo đơn không được giữ bộ đếm của đơn chưa thanh toán.
    public void Build_OrderCreatedBeforePayment_CountsPaymentMethodsOnPaymentDay()
    {
        OrderPaymentRow[] payments =
        [
            Payment(MiddayOct6, method: PaymentMethod.Cod),
            Payment(MiddayOct6, method: PaymentMethod.Sepay),
        ];
        CreatedOrderRow[] orders =
        [
            Created(MiddayOct5, method: PaymentMethod.Cod),
            Created(MiddayOct5, method: PaymentMethod.Sepay),
        ];

        var days = DailySalesAggregator.Build(ThreeDays, payments, orders);

        Assert.Equal(new PaymentMethodCounts(Cod: 0, Sepay: 0), DayOf(days, 5).PaymentMethods);
        Assert.Equal(new PaymentMethodCounts(Cod: 1, Sepay: 1), DayOf(days, 6).PaymentMethods);
    }

    [Fact]
    // Đơn chờ, đơn hủy và trạng thái không được ghi nhận doanh thu không được tăng bộ đếm COD hoặc SePay.
    public void Build_PaymentMethods_ExcludePendingCancelledAndUnrecognizedPayments()
    {
        CreatedOrderRow[] orders =
        [
            Created(MiddayOct5, OrderStatus.PendingConfirmation, PaymentMethod.Cod),
            Created(MiddayOct5, OrderStatus.Cancelled, PaymentMethod.Sepay, cancelReason: OrderReportMarkers.CustomerCancellationReason),
            Created(MiddayOct5, OrderStatus.Delivered, PaymentMethod.Cod),
            Created(MiddayOct5, OrderStatus.Returned, PaymentMethod.Sepay),
            Created(MiddayOct5, OrderStatus.Delivered, PaymentMethod.Cod),
        ];
        OrderPaymentRow[] payments =
        [
            Payment(MiddayOct5, status: PaymentStatus.RefundPending, method: PaymentMethod.Cod),
            Payment(MiddayOct5, status: PaymentStatus.Refunded, method: PaymentMethod.Sepay),
            Payment(MiddayOct5, status: PaymentStatus.Unpaid, method: PaymentMethod.Cod),
        ];

        var day = DayOf(DailySalesAggregator.Build(ThreeDays, payments, orders), 5);

        Assert.Equal(new PaymentMethodCounts(Cod: 0, Sepay: 0), day.PaymentMethods);
    }
}
