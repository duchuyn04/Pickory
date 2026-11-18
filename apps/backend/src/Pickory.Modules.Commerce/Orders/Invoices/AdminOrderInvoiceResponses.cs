namespace Pickory.Modules.Commerce.Orders.Invoices;

// Thông tin cửa hàng in ở đầu hóa đơn. Mọi trường là `null` khi chưa thiết lập cấu hình.
public sealed record OrderInvoiceShopResponse(
    string? Name,
    string? Address,
    string? Phone,
    string? TaxCode);

// Thông tin khách nhận hàng lấy từ bản chụp liên hệ lúc đặt đơn.
public sealed record OrderInvoiceCustomerResponse(
    string Name,
    string Phone,
    string Email);

// Thông tin xuất hóa đơn thuế giá trị gia tăng (VAT) của đơn hàng.
public sealed record OrderInvoiceVatResponse(
    string CompanyName,
    string TaxCode,
    string Address,
    string Email);

// Một dòng hàng trên hóa đơn in, gồm phân loại ghép chuỗi và thành tiền.
public sealed record OrderInvoiceLineResponse(
    string SkuCode,
    string Name,
    string? OptionText,
    int Quantity,
    int UnitPrice,
    int LineTotal);

// Dữ liệu đầy đủ để dựng một hóa đơn bán hàng cho một đơn hàng.
public sealed record OrderInvoiceResponse(
    string Code,
    DateTime CreatedAt,
    string Status,
    string PaymentMethod,
    string PaymentStatus,
    OrderInvoiceCustomerResponse Customer,
    string ShippingAddressText,
    OrderInvoiceVatResponse? VatInvoice,
    IReadOnlyList<OrderInvoiceLineResponse> Lines,
    int Subtotal,
    int ShippingFee,
    int ShippingDiscount,
    int Total);

// Kết quả trả về của route dữ liệu in hóa đơn gồm thông tin cửa hàng và danh sách hóa đơn.
public sealed record OrderInvoicesResponse(
    OrderInvoiceShopResponse Shop,
    IReadOnlyList<OrderInvoiceResponse> Invoices);
