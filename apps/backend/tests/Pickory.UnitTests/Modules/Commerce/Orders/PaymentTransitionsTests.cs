using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class PaymentTransitionsTests
{
    private static readonly PaymentStatus[] AllStatuses = Enum.GetValues<PaymentStatus>();
    private static readonly TransitionActor[] AllActors = Enum.GetValues<TransitionActor>();

    // Bảng mong đợi viết lại tay từ bảng trạng thái thanh toán của spec và bảng khớp webhook.
    private static readonly (PaymentStatus From, PaymentStatus To, TransitionActor Actor)[] ExpectedAllowed =
    [
        (PaymentStatus.Unpaid, PaymentStatus.Paid, TransitionActor.System),
        (PaymentStatus.Unpaid, PaymentStatus.Paid, TransitionActor.Staff),
        (PaymentStatus.Unpaid, PaymentStatus.RefundPending, TransitionActor.System),
        (PaymentStatus.Paid, PaymentStatus.RefundPending, TransitionActor.Customer),
        (PaymentStatus.Paid, PaymentStatus.RefundPending, TransitionActor.Staff),
        (PaymentStatus.Paid, PaymentStatus.RefundPending, TransitionActor.System),
        (PaymentStatus.RefundPending, PaymentStatus.Refunded, TransitionActor.Staff),
    ];

    [Fact]
    // Duyệt đủ mọi tổ hợp. Tổ hợp ngoài bảng phải bị từ chối.
    public void Evaluate_EveryCombination_MatchesTheTransitionTable()
    {
        foreach (var from in AllStatuses)
        {
            foreach (var to in AllStatuses)
            {
                foreach (var actor in AllActors)
                {
                    var expected = ExpectedAllowed.Contains((from, to, actor));

                    var decision = PaymentTransitions.Evaluate(from, to, actor);

                    Assert.True(expected == decision.IsAllowed, $"{from} -> {to} bởi {actor}: mong đợi {expected}.");
                }
            }
        }
    }

    [Fact]
    // `refunded` chỉ đến được từ `refund_pending`, và chỉ nhân viên có quyền hoàn tiền mới chuyển.
    public void Evaluate_ToRefunded_OnlyFromRefundPendingByStaff()
    {
        foreach (var from in AllStatuses)
        {
            foreach (var actor in AllActors)
            {
                var decision = PaymentTransitions.Evaluate(from, PaymentStatus.Refunded, actor);
                var expected = from == PaymentStatus.RefundPending && actor == TransitionActor.Staff;

                Assert.Equal(expected, decision.IsAllowed);
            }
        }
    }

    [Fact]
    // Hệ thống không được tự đánh dấu đã hoàn tiền. Spec cấm tự hoàn tiền khi chưa có người xác nhận.
    public void Evaluate_SystemCannotMarkRefunded()
    {
        var decision = PaymentTransitions.Evaluate(PaymentStatus.RefundPending, PaymentStatus.Refunded, TransitionActor.System);

        Assert.False(decision.IsAllowed);
        Assert.Equal(TransitionRejection.ActorNotAllowed, decision.Rejection);
    }

    [Fact]
    // Đã trả rồi không quay về chưa trả, và `refunded` là trạng thái cuối.
    public void Evaluate_BackwardAndTerminalPairs_AreRejected()
    {
        foreach (var actor in AllActors)
        {
            Assert.False(PaymentTransitions.Evaluate(PaymentStatus.Paid, PaymentStatus.Unpaid, actor).IsAllowed);
            Assert.False(PaymentTransitions.Evaluate(PaymentStatus.RefundPending, PaymentStatus.Paid, actor).IsAllowed);
            foreach (var to in AllStatuses)
            {
                Assert.False(PaymentTransitions.Evaluate(PaymentStatus.Refunded, to, actor).IsAllowed);
            }
        }
    }

    [Fact]
    // Cặp ngoài bảng báo lý do riêng, khác với cặp có trong bảng mà sai người chuyển.
    public void Evaluate_ReportsDifferentRejectionForTableAndActor()
    {
        var outsideTable = PaymentTransitions.Evaluate(PaymentStatus.Paid, PaymentStatus.Unpaid, TransitionActor.Staff);
        var wrongActor = PaymentTransitions.Evaluate(PaymentStatus.Unpaid, PaymentStatus.Paid, TransitionActor.Customer);

        Assert.Equal(TransitionRejection.NotInTransitionTable, outsideTable.Rejection);
        Assert.Equal(TransitionRejection.ActorNotAllowed, wrongActor.Rejection);
    }
}
