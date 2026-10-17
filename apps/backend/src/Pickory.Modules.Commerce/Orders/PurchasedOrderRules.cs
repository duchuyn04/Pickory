using System.Linq.Expressions;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;

namespace Pickory.Modules.Commerce.Orders;

// Bộ quy tắc xác định đơn hàng đã mua để lọc sản phẩm đã mua cho khách.
internal static class PurchasedOrderRules
{
    // Đơn được tính khi không bị hủy hoặc trả lại, và đã thanh toán hoặc đã nhận hàng.
    // Hàng đã trả lại không còn là hàng khách đã mua.
    public static readonly Expression<Func<Order, bool>> IsPurchased =
        order => order.Status != OrderStatus.Cancelled
            && order.Status != OrderStatus.Returned
            && (order.PaymentStatus == PaymentStatus.Paid || order.Status == OrderStatus.Delivered);
}
