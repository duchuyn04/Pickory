using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Demo;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Nhãn tiếng Việt của file xuất đơn hàng. Chữ của trạng thái và cờ khớp `apps/frontend/src/features/commerce/order-labels.ts`,
// để file Excel và màn danh sách gọi cùng một trạng thái bằng cùng một tên.
internal static class OrderExportLabels
{
    private const string NoteSeparator = ", ";

    private static readonly IReadOnlyDictionary<OrderStatus, string> OrderStatusLabels = new Dictionary<OrderStatus, string>
    {
        [OrderStatus.PendingPayment] = "Chờ thanh toán",
        [OrderStatus.PendingConfirmation] = "Chờ xác nhận",
        [OrderStatus.Confirmed] = "Đã xác nhận",
        [OrderStatus.Shipping] = "Đang giao hàng",
        [OrderStatus.Delivered] = "Đã giao hàng",
        [OrderStatus.Returned] = "Đã trả hàng",
        [OrderStatus.Cancelled] = "Đã hủy",
    };

    private static readonly IReadOnlyDictionary<PaymentStatus, string> PaymentStatusLabels = new Dictionary<PaymentStatus, string>
    {
        [PaymentStatus.Unpaid] = "Chưa thanh toán",
        [PaymentStatus.Paid] = "Đã thanh toán",
        [PaymentStatus.RefundPending] = "Chờ hoàn tiền",
        [PaymentStatus.Refunded] = "Đã hoàn tiền",
    };

    // Hai nhãn này theo spec và fixture của file xuất. Chúng khác nhãn của frontend ("Thanh toán khi nhận hàng (COD)").
    private static readonly IReadOnlyDictionary<PaymentMethod, string> PaymentMethodLabels = new Dictionary<PaymentMethod, string>
    {
        [PaymentMethod.Cod] = "COD",
        [PaymentMethod.Sepay] = "Chuyển khoản SePay",
    };

    private static readonly IReadOnlyDictionary<string, string> FlagLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["underpaid"] = "Thanh toán thiếu",
        ["overpaid"] = "Thanh toán dư",
        ["late_payment"] = "Thanh toán trễ",
        [OrderReportMarkers.PaymentExpiredFlag] = "Quá hạn thanh toán",
        [AdminOrderActionService.InvoiceIssuedFlag] = "Đã xuất hóa đơn",
        [ShipmentStatusChangedHandler.ShipmentCancelledFlag] = "Vận đơn đã hủy",
        [DemoOrderSeeder.DemoFlag] = "Đơn mẫu",
    };

    // Giá trị enum mới chưa có nhãn thì dùng nhãn snake_case. File vẫn xuất được, và test nhãn báo thiếu bảng.
    public static string ForOrderStatus(OrderStatus status) =>
        OrderStatusLabels.TryGetValue(status, out var label) ? label : OrderLabels.ToLabel(status);

    public static string ForPaymentStatus(PaymentStatus status) =>
        PaymentStatusLabels.TryGetValue(status, out var label) ? label : OrderLabels.ToLabel(status);

    public static string ForPaymentMethod(PaymentMethod method) =>
        PaymentMethodLabels.TryGetValue(method, out var label) ? label : OrderLabels.ToLabel(method);

    // Cờ ngoài bảng giữ nguyên mã. Nhân viên không mất thông tin khi backend thêm cờ mới.
    public static string ForFlag(string flag) =>
        FlagLabels.TryGetValue(flag, out var label) ? label : flag;

    // Cờ `invoice_issued` có cột riêng "Đã xuất hóa đơn VAT", nên cột ghi chú không lặp lại cờ này.
    public static bool HasInvoiceIssued(IReadOnlyList<string> flags) =>
        flags.Contains(AdminOrderActionService.InvoiceIssuedFlag, StringComparer.Ordinal);

    // Ghi chú nối nhãn của các cờ còn lại theo thứ tự lưu trong đơn. Đơn không còn cờ nào thì ô để trống.
    public static string? BuildOrderNote(IReadOnlyList<string> flags)
    {
        var labels = flags
            .Where(flag => !string.Equals(flag, AdminOrderActionService.InvoiceIssuedFlag, StringComparison.Ordinal))
            .Select(ForFlag)
            .ToList();
        return labels.Count == 0 ? null : string.Join(NoteSeparator, labels);
    }
}
