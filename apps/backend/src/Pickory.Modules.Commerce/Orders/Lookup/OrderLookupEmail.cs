using System.Text;
using Pickory.Shared.Email;

namespace Pickory.Modules.Commerce.Orders.Lookup;

// Dựng thư tra cứu đơn hàng dạng văn bản thuần. Hàm thuần, không đọc DB và không gửi thư.
public static class OrderLookupEmail
{
    public const string Purpose = "order-lookup";
    public const string PurposeKey = "purpose";
    public const string OrderCodeKey = "orderCode";

    // Dựng email thông báo kèm liên kết xem đơn hàng cho khách.
    public static EmailMessage Build(string orderCode, string contactEmail, string accessToken, string webOrigin)
    {
        var text = new StringBuilder();
        text.AppendLine("Chào bạn,");
        text.AppendLine();
        text.AppendLine($"Bạn hoặc ai đó vừa yêu cầu nhận lại liên kết xem đơn hàng {orderCode}.");
        text.AppendLine();
        text.AppendLine("Xem đơn hàng tại liên kết sau:");
        text.AppendLine(OrderLinkBuilder.Build(webOrigin, orderCode, accessToken));

        // Metadata cho hệ thống theo dõi. Token không nằm ở đây để tránh lộ token truy cập.
        var metadata = new Dictionary<string, string>
        {
            [PurposeKey] = Purpose,
            [OrderCodeKey] = orderCode,
        };

        return new EmailMessage(
            contactEmail,
            $"Pickory: liên kết xem đơn hàng {orderCode}",
            text.ToString(),
            metadata);
    }
}
