using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Contracts.Reports;
using Pickory.Shared.Calendar;

namespace Pickory.Modules.Commerce.Reports;

// Gom dòng đơn và dòng thanh toán thành số liệu từng ngày giờ Việt Nam. Không chạm DB để unit test kiểm được từng luật.
internal static class DailySalesAggregator
{
    // Doanh thu chỉ tính đơn `Paid`. `RefundPending` và `Refunded` là tiền sẽ trả hoặc đã trả lại khách, nên không tính.
    public static bool CountsAsRecognizedRevenue(PaymentStatus paymentStatus) => paymentStatus == PaymentStatus.Paid;

    // Đơn hết hạn thanh toán chưa từng là đơn khách giữ, nên không tính vào giá trị đơn đặt.
    // Đơn được khôi phục sau khi trả muộn đã mất cờ này và không còn `Cancelled`, nên vẫn được tính.
    public static bool IsPlacedOrder(CreatedOrderRow order) =>
        !(order.Status == OrderStatus.Cancelled && order.HasPaymentExpiredFlag);

    public static IReadOnlyList<DailySales> Build(
        DateRange range, IReadOnlyList<OrderPaymentRow> payments, IReadOnlyList<CreatedOrderRow> createdOrders)
    {
        // Tạo sẵn một bộ cộng cho mỗi ngày để ngày không có số liệu vẫn có dòng toàn số 0.
        var totalsByDay = new Dictionary<DateOnly, DayTotals>();
        for (var day = range.From; day <= range.To; day = day.AddDays(1))
        {
            totalsByDay[day] = new DayTotals();
        }

        foreach (var payment in payments)
        {
            // Ngày thanh toán tính theo giờ Việt Nam để khớp biên nửa đêm với bộ lọc SQL.
            // Dòng ngoài khoảng bị bỏ qua, vì bộ lọc SQL đã chặn và lớp gom không tự mở rộng khoảng.
            if (!totalsByDay.TryGetValue(VietnamCalendar.ToVietnamDate(payment.PaidAtUtc), out var totals))
            {
                continue;
            }

            // Dòng của đơn hoàn tiền vẫn được đọc từ SQL. Chỉ đơn đủ điều kiện mới cộng vào doanh thu.
            if (CountsAsRecognizedRevenue(payment.PaymentStatus))
            {
                totals.RecognizedRevenue += payment.Total;
                totals.PaidOrderCount++;
            }
        }

        foreach (var order in createdOrders)
        {
            // Ngày tạo đơn cũng tính theo giờ Việt Nam. Đơn ngoài khoảng bị bỏ qua vì lớp gom không tự mở rộng khoảng.
            if (!totalsByDay.TryGetValue(VietnamCalendar.ToVietnamDate(order.CreatedAtUtc), out var totals))
            {
                continue;
            }

            // Số đơn tạo là mẫu số của tỉ lệ hủy, nên đếm mọi đơn kể cả đơn hết hạn.
            totals.CreatedOrderCount++;

            // Lý do hủy chỉ có nghĩa với đơn đang ở trạng thái hủy. Đơn đã khôi phục có thể còn dòng lịch sử hủy cũ.
            if (order.Status == OrderStatus.Cancelled)
            {
                CountCancellation(totals, order);
            }

            // Đơn hết hạn thanh toán không vào giá trị đơn đặt, giảm ship và phương thức thanh toán. Số đơn tạo và số đơn hủy ở trên đã đếm đơn đó.
            if (!IsPlacedOrder(order))
            {
                continue;
            }

            totals.PlacedOrderValue += order.Total;
            totals.ShippingDiscount += order.ShippingDiscount;
            // Mỗi phương thức có một bộ đếm riêng. Giá trị lạ ném lỗi để phương thức mới không bị đếm nhầm thành COD một cách im lặng.
            switch (order.PaymentMethod)
            {
                case PaymentMethod.Sepay:
                    totals.SepayOrderCount++;
                    break;
                case PaymentMethod.Cod:
                    totals.CodOrderCount++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Phương thức thanh toán {order.PaymentMethod} chưa có bộ đếm trong báo cáo.");
            }
        }

        // Dictionary không đảm bảo thứ tự, nên sắp tăng dần theo ngày trước khi trả.
        return totalsByDay
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Value.ToDailySales(entry.Key))
            .ToList();
    }

    private static void CountCancellation(DayTotals totals, CreatedOrderRow order)
    {
        var reason = CancellationReasonClassifier.Classify(
            order.HasPaymentExpiredFlag, order.LatestCancellationHistoryReason);
        // Mỗi lý do có một bộ đếm riêng để bên dùng tính tỉ lệ hủy theo từng lý do. Lý do mới phải thêm bộ đếm trước khi dùng.
        switch (reason)
        {
            case CancellationReason.PaymentExpired:
                totals.PaymentExpiredCancellations++;
                break;
            case CancellationReason.Customer:
                totals.CustomerCancellations++;
                break;
            case CancellationReason.Shop:
                totals.ShopCancellations++;
                break;
            default:
                throw new InvalidOperationException($"Lý do hủy {reason} chưa có bộ đếm trong báo cáo.");
        }
    }

    // Bộ cộng của một ngày. Số tiền dùng `long` vì tổng nhiều đơn `int` có thể tràn.
    private sealed class DayTotals
    {
        public long RecognizedRevenue { get; set; }
        public int PaidOrderCount { get; set; }
        public long PlacedOrderValue { get; set; }
        public int CreatedOrderCount { get; set; }
        public int CustomerCancellations { get; set; }
        public int ShopCancellations { get; set; }
        public int PaymentExpiredCancellations { get; set; }
        public int CodOrderCount { get; set; }
        public int SepayOrderCount { get; set; }
        public long ShippingDiscount { get; set; }

        public DailySales ToDailySales(DateOnly day) => new(
            day,
            RecognizedRevenue,
            PaidOrderCount,
            PlacedOrderValue,
            CreatedOrderCount,
            new CancellationCounts(CustomerCancellations, ShopCancellations, PaymentExpiredCancellations),
            new PaymentMethodCounts(CodOrderCount, SepayOrderCount),
            ShippingDiscount);
    }
}
