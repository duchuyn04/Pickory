namespace Pickory.Modules.Commerce.Contracts.Orders;

// Trạng thái thanh toán của đơn, độc lập với trạng thái đơn. Nhãn trong DB là snake_case, ví dụ `refund_pending`.
public enum PaymentStatus
{
    Unpaid,
    Paid,
    RefundPending,
    Refunded,
}
