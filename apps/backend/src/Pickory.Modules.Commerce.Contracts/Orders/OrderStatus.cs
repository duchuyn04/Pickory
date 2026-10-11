namespace Pickory.Modules.Commerce.Contracts.Orders;

// Trạng thái đơn hàng. Nhãn trong DB là snake_case của tên giá trị, ví dụ `pending_payment`.
public enum OrderStatus
{
    PendingPayment,
    PendingConfirmation,
    Confirmed,
    Shipping,
    Delivered,
    Returned,
    Cancelled,
}
