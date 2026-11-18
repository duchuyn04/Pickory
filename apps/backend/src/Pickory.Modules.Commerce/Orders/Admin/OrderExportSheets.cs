using System.Text.Json;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Một đơn ở sheet "Đơn hàng". Mọi giá trị đã ở dạng hiển thị, nên cột chỉ đọc thuộc tính mà không tính thêm.
internal sealed record OrderExportRow(
    string Code,
    DateTime CreatedAt,
    string Status,
    string PaymentStatus,
    string PaymentMethod,
    string? CustomerName,
    string? Phone,
    string? Email,
    string ShippingAddress,
    long ItemCount,
    int Subtotal,
    int ShippingFee,
    int ShippingDiscount,
    int Total,
    string? InvoiceCompanyName,
    string? InvoiceTaxCode,
    string? InvoiceAddress,
    string? InvoiceEmail,
    bool InvoiceIssued,
    string? Note);

// Một dòng hàng ở sheet "Chi tiết sản phẩm". Mã đơn và ngày đặt lấy từ đơn để lọc theo đơn được trong Excel.
internal sealed record OrderExportLine(
    string OrderCode,
    DateTime OrderCreatedAt,
    string SkuCode,
    string ProductName,
    string? Variant,
    int Quantity,
    int UnitPrice);

// Tên sheet, tiêu đề và thứ tự cột theo mục "Cột của từng file" của `spec/SPEC-exports-printing.md`.
internal static class OrderExportSheets
{
    public const string OrdersSheetName = "Đơn hàng";
    public const string LinesSheetName = "Chi tiết sản phẩm";

    // Dấu ngăn cách của địa chỉ và phân loại, theo spec ("Đen, 256 GB").
    private const string ListSeparator = ", ";

    private static readonly IReadOnlyList<ExportColumn<OrderExportRow>> OrderColumns =
    [
        ExportColumn<OrderExportRow>.Text("Mã đơn", 16, row => row.Code),
        ExportColumn<OrderExportRow>.DateTime("Ngày đặt", 18, row => row.CreatedAt),
        ExportColumn<OrderExportRow>.Text("Trạng thái đơn", 18, row => row.Status),
        ExportColumn<OrderExportRow>.Text("Trạng thái thanh toán", 22, row => row.PaymentStatus),
        ExportColumn<OrderExportRow>.Text("Phương thức thanh toán", 24, row => row.PaymentMethod),
        ExportColumn<OrderExportRow>.Text("Tên khách", 24, row => row.CustomerName),
        // Số điện thoại là chữ. Kiểu chữ giữ số 0 ở đầu số.
        ExportColumn<OrderExportRow>.Text("Số điện thoại", 16, row => row.Phone),
        ExportColumn<OrderExportRow>.Text("Email", 28, row => row.Email),
        ExportColumn<OrderExportRow>.Text("Địa chỉ giao", 50, row => row.ShippingAddress),
        ExportColumn<OrderExportRow>.Integer("Số sản phẩm", 14, row => row.ItemCount),
        ExportColumn<OrderExportRow>.Money("Tạm tính", 16, row => row.Subtotal),
        ExportColumn<OrderExportRow>.Money("Phí vận chuyển", 16, row => row.ShippingFee),
        ExportColumn<OrderExportRow>.Money("Giảm phí vận chuyển", 20, row => row.ShippingDiscount),
        ExportColumn<OrderExportRow>.Money("Tổng tiền", 16, row => row.Total),
        ExportColumn<OrderExportRow>.Text("Tên công ty", 30, row => row.InvoiceCompanyName),
        // Mã số thuế là chữ. Excel không cắt số 0 ở đầu mã.
        ExportColumn<OrderExportRow>.Text("Mã số thuế", 16, row => row.InvoiceTaxCode),
        ExportColumn<OrderExportRow>.Text("Địa chỉ xuất hóa đơn", 40, row => row.InvoiceAddress),
        ExportColumn<OrderExportRow>.Text("Email nhận hóa đơn", 28, row => row.InvoiceEmail),
        ExportColumn<OrderExportRow>.YesNo("Đã xuất hóa đơn VAT", 20, row => row.InvoiceIssued),
        ExportColumn<OrderExportRow>.Text("Ghi chú đơn", 36, row => row.Note),
    ];

    private static readonly IReadOnlyList<ExportColumn<OrderExportLine>> LineColumns =
    [
        ExportColumn<OrderExportLine>.Text("Mã đơn", 16, line => line.OrderCode),
        ExportColumn<OrderExportLine>.DateTime("Ngày đặt", 18, line => line.OrderCreatedAt),
        ExportColumn<OrderExportLine>.Text("Mã SKU", 20, line => line.SkuCode),
        ExportColumn<OrderExportLine>.Text("Tên sản phẩm", 40, line => line.ProductName),
        ExportColumn<OrderExportLine>.Text("Phân loại", 24, line => line.Variant),
        ExportColumn<OrderExportLine>.Integer("Số lượng", 12, line => line.Quantity),
        ExportColumn<OrderExportLine>.Money("Đơn giá", 16, line => line.UnitPrice),
        // Nhân bằng `long`. Đơn giá nhân số lượng có thể vượt `int.MaxValue`.
        ExportColumn<OrderExportLine>.Money("Thành tiền", 16, line => (long)line.UnitPrice * line.Quantity),
    ];

    public static ExportSheet BuildOrdersSheet(IEnumerable<OrderExportRow> rows) =>
        ExportSheet.Create(OrdersSheetName, OrderColumns, rows);

    public static ExportSheet BuildLinesSheet(IEnumerable<OrderExportLine> lines) =>
        ExportSheet.Create(LinesSheetName, LineColumns, lines);

    // Ghép các phần địa chỉ từ nhỏ tới lớn: số nhà, phường, tỉnh. Phần rỗng bị bỏ để không có dấu phẩy thừa.
    public static string JoinShippingAddress(string? line, string? wardName, string? provinceName)
    {
        var parts = new[] { line, wardName, provinceName }.Where(part => !string.IsNullOrWhiteSpace(part));
        return string.Join(ListSeparator, parts);
    }

    // Giá trị chữ không rỗng cho kết quả giống `OrderItemMapper.FormatVariantName`, nên trang chi tiết đơn và file hiện cùng phân loại.
    // Hàm đó trả `null` khi có giá trị không phải chữ, còn file xuất giữ giá trị đó.
    public static string? JoinVariant(JsonDocument optionValues)
    {
        // Phân loại không phải object (dữ liệu lỗi) thì không có tên và giá trị để ghép.
        if (optionValues.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var property in optionValues.RootElement.EnumerateObject())
        {
            // Giá trị chữ lấy nguyên chữ. Kiểu khác (số, logic) lấy dạng JSON để không mất thông tin.
            var text = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.GetRawText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                values.Add(text);
            }
        }

        return values.Count == 0 ? null : string.Join(ListSeparator, values);
    }
}
