namespace Pickory.Modules.ActivityTracking.Metrics;

// Tên ba chỉ số đếm theo ngày. Giá trị này là khóa lưu trong bảng đếm, nên đổi chữ là mất liên kết với số liệu cũ.
internal static class ActivityMetricNames
{
    public const string ProductView = "product_view";
    public const string AddToCart = "add_to_cart";
    public const string OrderPlaced = "order_placed";
}
