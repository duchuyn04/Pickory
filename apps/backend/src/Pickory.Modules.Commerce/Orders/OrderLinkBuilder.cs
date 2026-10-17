namespace Pickory.Modules.Commerce.Orders;

// Dựng liên kết xem đơn có token. Thư xác nhận và thư tra cứu dùng chung một dạng link.
public static class OrderLinkBuilder
{
    // Mã đơn và token đi qua `EscapeDataString` để ký tự lạ không làm đổi nghĩa URL.
    public static string Build(string webOrigin, string orderCode, string accessToken)
    {
        return $"{webOrigin.TrimEnd('/')}/orders/{Uri.EscapeDataString(orderCode)}?t={Uri.EscapeDataString(accessToken)}";
    }
}
