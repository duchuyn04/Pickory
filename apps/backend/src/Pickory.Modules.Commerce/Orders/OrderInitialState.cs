using Pickory.Modules.Commerce.Checkout;
using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Orders;

// Trạng thái ban đầu của đơn mới theo phương thức thanh toán. Hàm thuần, nên unit test không cần đồng hồ thật.
public sealed record OrderInitialState(OrderStatus Status, PaymentStatus PaymentStatus, DateTime? PaymentDeadline)
{
    // Khách SePay có 30 phút để chuyển khoản. Hết hạn thì job hết hạn hủy đơn và trả hàng.
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(30);

    public static OrderInitialState For(string paymentMethod, DateTime now)
    {
        return paymentMethod switch
        {
            // COD không cần tiền về trước. Đơn chờ nhân viên xác nhận và không có hạn thanh toán.
            CheckoutPaymentMethods.Cod =>
                new OrderInitialState(OrderStatus.PendingConfirmation, PaymentStatus.Unpaid, PaymentDeadline: null),

            // SePay cần tiền về trước. Đơn chờ thanh toán tới hết hạn.
            CheckoutPaymentMethods.Sepay =>
                new OrderInitialState(OrderStatus.PendingPayment, PaymentStatus.Unpaid, now + PaymentWindow),

            _ => throw new ArgumentOutOfRangeException(nameof(paymentMethod), paymentMethod, "Phương thức thanh toán không được hỗ trợ."),
        };
    }
}
