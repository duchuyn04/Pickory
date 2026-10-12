using System.Globalization;
using System.Text;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Shared.Email;

namespace Pickory.Modules.Commerce.Orders;

// Một dòng hàng trong thư xác nhận. Tên là bản chụp lúc đặt, nên thư không đổi khi catalog đổi.
public sealed record OrderConfirmationLine(string Name, int Quantity, int UnitPrice);

// Dữ liệu dựng thư xác nhận. `AccessToken` là token gốc, chỉ có trong bộ nhớ lúc tạo đơn và không bao giờ được ghi log.
public sealed record OrderConfirmationInput(
    string OrderCode,
    string ContactEmail,
    IReadOnlyList<OrderConfirmationLine> Lines,
    int Subtotal,
    int ShippingFee,
    int ShippingDiscount,
    int Total,
    PaymentMethod PaymentMethod,
    string AccessToken);

// Dựng thư xác nhận đơn dạng văn bản thuần. Hàm thuần, không đọc DB và không gửi thư.
public static class OrderConfirmationEmail
{
    public const string Purpose = "order-confirmation";
    public const string PurposeKey = "purpose";
    public const string OrderCodeKey = "orderCode";

    // Dấu chấm ngăn nhóm nghìn như văn hóa `vi-VN`. Khai báo tường minh để kết quả không phụ thuộc dữ liệu văn hóa của máy.
    private static readonly NumberFormatInfo MoneyFormat = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
    };

    public static EmailMessage Build(OrderConfirmationInput input, string webOrigin)
    {
        var text = new StringBuilder();
        text.AppendLine("Chào bạn,");
        text.AppendLine();
        text.AppendLine("Pickory đã nhận đơn hàng của bạn.");
        text.AppendLine();
        text.AppendLine($"Mã đơn hàng: {input.OrderCode}");
        text.AppendLine();
        text.AppendLine("Sản phẩm:");
        foreach (var line in input.Lines)
        {
            text.AppendLine($"- {line.Name}");
            text.AppendLine($"  Số lượng: {line.Quantity}");
            text.AppendLine($"  Đơn giá: {FormatMoney(line.UnitPrice)}");
        }

        text.AppendLine();
        text.AppendLine($"Tạm tính: {FormatMoney(input.Subtotal)}");
        text.AppendLine($"Phí vận chuyển: {FormatMoney(input.ShippingFee)}");

        // Dòng miễn phí chỉ có khi đơn thật sự được giảm. Đơn không giảm thì dòng này chỉ gây hiểu nhầm.
        if (input.ShippingDiscount > 0)
        {
            text.AppendLine($"Miễn phí vận chuyển: -{FormatMoney(input.ShippingDiscount)}");
        }

        text.AppendLine($"Tổng tiền: {FormatMoney(input.Total)}");
        text.AppendLine($"Phương thức thanh toán: {PaymentMethodLabel(input.PaymentMethod)}");
        text.AppendLine();
        text.AppendLine("Xem đơn hàng tại liên kết sau:");
        text.AppendLine(BuildOrderLink(webOrigin, input.OrderCode, input.AccessToken));

        // Metadata cho máy đọc. Token không nằm ở đây, nên test và outbox dev lấy link từ thân thư.
        var metadata = new Dictionary<string, string>
        {
            [PurposeKey] = Purpose,
            [OrderCodeKey] = input.OrderCode,
        };

        return new EmailMessage(
            input.ContactEmail,
            $"Pickory: xác nhận đơn hàng {input.OrderCode}",
            text.ToString(),
            metadata);
    }

    private static string FormatMoney(int amount) =>
        amount.ToString("N0", MoneyFormat) + " đ";

    private static string PaymentMethodLabel(PaymentMethod method) =>
        method switch
        {
            PaymentMethod.Cod => "Thanh toán khi nhận hàng (COD)",
            PaymentMethod.Sepay => "Chuyển khoản (VietQR)",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Phương thức thanh toán không được hỗ trợ."),
        };

    private static string BuildOrderLink(string webOrigin, string orderCode, string accessToken) =>
        // Mã đơn và token đi qua `EscapeDataString`, để ký tự lạ không đổi nghĩa của đường dẫn hay query.
        $"{webOrigin.TrimEnd('/')}/orders/{Uri.EscapeDataString(orderCode)}?t={Uri.EscapeDataString(accessToken)}";
}
