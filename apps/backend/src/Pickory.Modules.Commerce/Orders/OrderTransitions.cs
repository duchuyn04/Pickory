using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Orders;

// Máy trạng thái của đơn hàng, viết thành hàm thuần để unit test duyệt đủ mọi cặp trạng thái.
// Bảng lấy từ `spec/SPEC-commerce.md`, mục "Trạng thái đơn hàng".
public static class OrderTransitions
{
    private sealed record Rule(OrderStatus From, OrderStatus To, TransitionActor[] Actors);

    private static readonly TransitionActor[] SystemOnly = [TransitionActor.System];
    private static readonly TransitionActor[] StaffOnly = [TransitionActor.Staff];
    private static readonly TransitionActor[] StaffOrSystem = [TransitionActor.Staff, TransitionActor.System];
    private static readonly TransitionActor[] CustomerOrStaff = [TransitionActor.Customer, TransitionActor.Staff];
    private static readonly TransitionActor[] AnyActor = [TransitionActor.Customer, TransitionActor.Staff, TransitionActor.System];

    private static readonly Rule[] Rules =
    [
        // Tiền SePay về đủ thì hệ thống đưa đơn sang chờ xác nhận.
        new(OrderStatus.PendingPayment, OrderStatus.PendingConfirmation, SystemOnly),
        // Mọi bên đều hủy được đơn chưa trả tiền. Hệ thống hủy khi quá hạn 30 phút.
        new(OrderStatus.PendingPayment, OrderStatus.Cancelled, AnyActor),
        new(OrderStatus.PendingConfirmation, OrderStatus.Confirmed, StaffOnly),
        // Khách chỉ hủy được tới khi shop chưa xác nhận. Từ `confirmed` trở đi chỉ nhân viên hủy.
        new(OrderStatus.PendingConfirmation, OrderStatus.Cancelled, CustomerOrStaff),
        new(OrderStatus.Confirmed, OrderStatus.Shipping, StaffOnly),
        new(OrderStatus.Confirmed, OrderStatus.Cancelled, StaffOnly),
        // Trạng thái giao hàng do `shipping` báo về. Con người không đặt tay hai trạng thái này.
        new(OrderStatus.Shipping, OrderStatus.Delivered, SystemOnly),
        new(OrderStatus.Shipping, OrderStatus.Returned, SystemOnly),
        // Hủy vận đơn thành công thì đơn về `confirmed`. GHN tự hủy vận đơn cũng đưa đơn về đây.
        new(OrderStatus.Shipping, OrderStatus.Confirmed, StaffOrSystem),
        // Đơn đang giao chỉ hủy được khi vận đơn không còn hiệu lực. Điều kiện này kiểm riêng trong `Evaluate`.
        new(OrderStatus.Shipping, OrderStatus.Cancelled, StaffOnly),
        // Tiền về muộn cho đơn đã bị hủy vì quá hạn, và còn đủ hàng thì hệ thống khôi phục đơn.
        // Lý do hủy là quá hạn thanh toán do bên gọi kiểm, vì bảng này chỉ biết cặp trạng thái.
        new(OrderStatus.Cancelled, OrderStatus.PendingConfirmation, SystemOnly),
    ];

    public static TransitionDecision Evaluate(
        OrderStatus from, OrderStatus to, TransitionActor actor, bool hasActiveShipment)
    {
        var rule = Rules.FirstOrDefault(candidate => candidate.From == from && candidate.To == to);

        // Cặp ngoài bảng gồm cả chuyển sang chính trạng thái hiện tại. Trả 409 để đơn không rơi vào trạng thái vô nghĩa.
        if (rule is null)
        {
            return TransitionDecision.Rejected(TransitionRejection.NotInTransitionTable);
        }

        // Người chuyển không có tên trong dòng của bảng thì bị từ chối, ví dụ khách tự xác nhận đơn của mình.
        if (!rule.Actors.Contains(actor))
        {
            return TransitionDecision.Rejected(TransitionRejection.ActorNotAllowed);
        }

        // Hủy đơn khi vận đơn GHN còn hiệu lực làm hàng đã giao cho GHN mà shop coi như chưa gửi. Phải hủy vận đơn trước.
        var cancelsShippingOrder = from == OrderStatus.Shipping && to == OrderStatus.Cancelled;
        if (cancelsShippingOrder && hasActiveShipment)
        {
            return TransitionDecision.Rejected(TransitionRejection.ShipmentStillActive);
        }

        return TransitionDecision.Allowed;
    }

    // Trạng thái thanh toán mới sau khi đơn chuyển sang `newOrderStatus`.
    // Đơn đã trả tiền mà bị hủy hoặc hoàn thì tiền phải trả lại cho khách, nên thanh toán chuyển sang chờ hoàn tiền.
    public static PaymentStatus PaymentStatusAfterOrderChange(OrderStatus newOrderStatus, PaymentStatus currentPaymentStatus)
    {
        var orderEndedWithoutDelivery = newOrderStatus is OrderStatus.Cancelled or OrderStatus.Returned;
        if (orderEndedWithoutDelivery && currentPaymentStatus == PaymentStatus.Paid)
        {
            return PaymentStatus.RefundPending;
        }

        return currentPaymentStatus;
    }
}
