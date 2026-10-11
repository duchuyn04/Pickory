using Pickory.Modules.Commerce.Contracts.Orders;

namespace Pickory.Modules.Commerce.Orders;

// Máy trạng thái thanh toán, viết thành hàm thuần. Bảng lấy từ mục "Trạng thái đơn hàng" và bảng khớp webhook của spec.
public static class PaymentTransitions
{
    private sealed record Rule(PaymentStatus From, PaymentStatus To, TransitionActor[] Actors);

    private static readonly Rule[] Rules =
    [
        // Tiền về qua webhook thì hệ thống ghi nhận. Nhân viên ghi nhận khi khớp tay hoặc nhận tiền COD từ GHN.
        new(PaymentStatus.Unpaid, PaymentStatus.Paid, [TransitionActor.System, TransitionActor.Staff]),
        // Tiền về cho đơn đã hủy vì quá hạn mà hết hàng. Hệ thống đánh dấu để nhân viên hoàn tiền.
        new(PaymentStatus.Unpaid, PaymentStatus.RefundPending, [TransitionActor.System]),
        // Hủy hoặc hoàn đơn đã trả tiền. Bên hủy đơn đã được bảng trạng thái đơn kiểm, nên mọi bên đều được ghi kết quả này.
        new(PaymentStatus.Paid, PaymentStatus.RefundPending, [TransitionActor.Customer, TransitionActor.Staff, TransitionActor.System]),
        // Chỉ người có quyền hoàn tiền đánh dấu `refunded`. Spec cấm hệ thống tự hoàn tiền khi chưa có người xác nhận.
        new(PaymentStatus.RefundPending, PaymentStatus.Refunded, [TransitionActor.Staff]),
    ];

    public static TransitionDecision Evaluate(PaymentStatus from, PaymentStatus to, TransitionActor actor)
    {
        var rule = Rules.FirstOrDefault(candidate => candidate.From == from && candidate.To == to);

        // Cặp ngoài bảng gồm mọi đường đi vào `refunded` không qua `refund_pending`.
        if (rule is null)
        {
            return TransitionDecision.Rejected(TransitionRejection.NotInTransitionTable);
        }

        if (!rule.Actors.Contains(actor))
        {
            return TransitionDecision.Rejected(TransitionRejection.ActorNotAllowed);
        }

        return TransitionDecision.Allowed;
    }
}
