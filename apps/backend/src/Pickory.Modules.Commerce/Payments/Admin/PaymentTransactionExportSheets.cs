using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Orders.Admin;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Commerce.Payments.Admin;

// Một giao dịch đọc từ DB cho file xuất, kèm tóm tắt đơn đã gắn. Mọi trường đơn là null khi giao dịch chưa gắn đơn.
internal sealed record PaymentTransactionExportSource(
    string ProviderTxId,
    DateTime ReceivedAt,
    int Amount,
    string Content,
    string MatchStatus,
    string? OrderCode,
    int? OrderTotal,
    PaymentStatus? OrderPaymentStatus,
    IReadOnlyList<string>? OrderFlags);

// Một giao dịch ở sheet "Giao dịch SePay". Mọi giá trị đã ở dạng hiển thị, nên cột chỉ đọc thuộc tính mà không tính thêm.
internal sealed record PaymentTransactionExportRow(
    string ProviderTxId,
    DateTime ReceivedAt,
    int Amount,
    string Content,
    string MatchStatus,
    string? OrderCode,
    int? OrderTotal,
    string? OrderPaymentStatus,
    string? OrderNote);

// Tên sheet, tiêu đề và thứ tự cột theo mục "File giao dịch thanh toán" của `spec/SPEC-exports-printing.md`.
internal static class PaymentTransactionExportSheets
{
    public const string SheetName = "Giao dịch SePay";

    private static readonly IReadOnlyList<ExportColumn<PaymentTransactionExportRow>> Columns =
    [
        ExportColumn<PaymentTransactionExportRow>.Text("Mã giao dịch SePay", 24, row => row.ProviderTxId),
        ExportColumn<PaymentTransactionExportRow>.DateTime("Thời điểm nhận tiền", 20, row => row.ReceivedAt),
        ExportColumn<PaymentTransactionExportRow>.Money("Số tiền", 16, row => row.Amount),
        ExportColumn<PaymentTransactionExportRow>.Text("Nội dung chuyển khoản", 44, row => row.Content),
        ExportColumn<PaymentTransactionExportRow>.Text("Trạng thái khớp", 18, row => row.MatchStatus),
        // Bốn cột đơn dưới đây là null khi giao dịch chưa gắn đơn. Ô để trống, không ghi số 0 hay chuỗi rỗng.
        ExportColumn<PaymentTransactionExportRow>.Text("Mã đơn", 16, row => row.OrderCode),
        ExportColumn<PaymentTransactionExportRow>.Money("Tổng tiền đơn", 16, row => row.OrderTotal),
        ExportColumn<PaymentTransactionExportRow>.Text("Trạng thái thanh toán của đơn", 28, row => row.OrderPaymentStatus),
        ExportColumn<PaymentTransactionExportRow>.Text("Ghi chú đơn", 36, row => row.OrderNote),
    ];

    public static ExportSheet BuildSheet(IEnumerable<PaymentTransactionExportRow> rows) =>
        ExportSheet.Create(SheetName, Columns, rows);

    // Đổi giao dịch đọc từ DB thành dòng hiển thị. Giao dịch chưa gắn đơn giữ bốn trường đơn là null để ô trống.
    public static PaymentTransactionExportRow BuildRow(PaymentTransactionExportSource source) =>
        new(
            source.ProviderTxId,
            source.ReceivedAt,
            source.Amount,
            source.Content,
            PaymentTransactionExportLabels.ForMatchStatus(source.MatchStatus),
            source.OrderCode,
            source.OrderTotal,
            // Nhãn và ghi chú dùng chung nguồn với file xuất đơn. Hai file gọi cùng một trạng thái bằng cùng một tên.
            source.OrderPaymentStatus is { } paymentStatus ? OrderExportLabels.ForPaymentStatus(paymentStatus) : null,
            source.OrderFlags is { } flags ? OrderExportLabels.BuildOrderNote(flags) : null);
}
