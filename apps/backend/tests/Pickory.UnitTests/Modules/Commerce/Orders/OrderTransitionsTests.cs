using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Orders;
using Xunit;

namespace Pickory.UnitTests.Modules.Commerce.Orders;

public sealed class OrderTransitionsTests
{
    private static readonly OrderStatus[] AllStatuses = Enum.GetValues<OrderStatus>();
    private static readonly TransitionActor[] AllActors = Enum.GetValues<TransitionActor>();

    // Bảng mong đợi viết lại tay từ bảng của spec, độc lập với mã nguồn. Mỗi dòng là một cặp và người được chuyển.
    private static readonly (OrderStatus From, OrderStatus To, TransitionActor Actor)[] ExpectedAllowed =
    [
        (OrderStatus.PendingPayment, OrderStatus.PendingConfirmation, TransitionActor.System),
        (OrderStatus.PendingPayment, OrderStatus.Cancelled, TransitionActor.Customer),
        (OrderStatus.PendingPayment, OrderStatus.Cancelled, TransitionActor.Staff),
        (OrderStatus.PendingPayment, OrderStatus.Cancelled, TransitionActor.System),
        (OrderStatus.PendingConfirmation, OrderStatus.Confirmed, TransitionActor.Staff),
        (OrderStatus.PendingConfirmation, OrderStatus.Cancelled, TransitionActor.Customer),
        (OrderStatus.PendingConfirmation, OrderStatus.Cancelled, TransitionActor.Staff),
        (OrderStatus.Confirmed, OrderStatus.Shipping, TransitionActor.Staff),
        (OrderStatus.Confirmed, OrderStatus.Cancelled, TransitionActor.Staff),
        (OrderStatus.Shipping, OrderStatus.Delivered, TransitionActor.System),
        (OrderStatus.Shipping, OrderStatus.Returned, TransitionActor.System),
        (OrderStatus.Shipping, OrderStatus.Confirmed, TransitionActor.Staff),
        (OrderStatus.Shipping, OrderStatus.Confirmed, TransitionActor.System),
        (OrderStatus.Shipping, OrderStatus.Cancelled, TransitionActor.Staff),
        (OrderStatus.Cancelled, OrderStatus.PendingConfirmation, TransitionActor.System),
    ];

    private static bool IsExpectedAllowed(OrderStatus from, OrderStatus to, TransitionActor actor) =>
        ExpectedAllowed.Contains((from, to, actor));

    [Fact]
    // Duyệt đủ mọi tổ hợp trạng thái cũ, trạng thái mới và người chuyển. Tổ hợp ngoài bảng phải bị từ chối.
    public void Evaluate_EveryCombination_MatchesTheTransitionTable()
    {
        foreach (var from in AllStatuses)
        {
            foreach (var to in AllStatuses)
            {
                foreach (var actor in AllActors)
                {
                    // Vận đơn đã hủy để cặp `shipping` sang `cancelled` chỉ phụ thuộc bảng, không phụ thuộc vận đơn.
                    var decision = OrderTransitions.Evaluate(from, to, actor, hasActiveShipment: false);

                    Assert.True(
                        IsExpectedAllowed(from, to, actor) == decision.IsAllowed,
                        $"{from} -> {to} bởi {actor}: mong đợi {IsExpectedAllowed(from, to, actor)}, thực tế {decision.IsAllowed}.");
                }
            }
        }
    }

    [Fact]
    // Bảng mong đợi có đúng 15 dòng khác nhau. Dòng bị trùng hoặc bị xóa nhầm trong test sẽ lộ ra.
    public void ExpectedTable_HasFifteenAllowedEntries()
    {
        Assert.Equal(15, ExpectedAllowed.Length);
        Assert.Equal(15, ExpectedAllowed.Distinct().Count());
    }

    [Fact]
    // Chuyển từ trạng thái sang chính nó không phải chuyển đổi, nên luôn bị từ chối.
    public void Evaluate_SameStatus_IsRejected()
    {
        foreach (var status in AllStatuses)
        {
            foreach (var actor in AllActors)
            {
                Assert.False(OrderTransitions.Evaluate(status, status, actor, hasActiveShipment: false).IsAllowed);
            }
        }
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment, true)]
    [InlineData(OrderStatus.PendingConfirmation, true)]
    [InlineData(OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Shipping, false)]
    [InlineData(OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Returned, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    // Khách chỉ hủy được khi đơn chờ thanh toán hoặc chờ xác nhận. Từ `confirmed` trở đi khách phải liên hệ shop.
    public void Evaluate_CustomerCancel_OnlyBeforeConfirmation(OrderStatus from, bool expected)
    {
        var decision = OrderTransitions.Evaluate(from, OrderStatus.Cancelled, TransitionActor.Customer, hasActiveShipment: false);

        Assert.Equal(expected, decision.IsAllowed);
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment, true)]
    [InlineData(OrderStatus.PendingConfirmation, true)]
    [InlineData(OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Returned, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    // Nhân viên hủy được mọi đơn chưa giao. Đơn đã giao, đã hoàn hoặc đã hủy thì không.
    public void Evaluate_StaffCancel_AnyOrderBeforeDelivered(OrderStatus from, bool expected)
    {
        var decision = OrderTransitions.Evaluate(from, OrderStatus.Cancelled, TransitionActor.Staff, hasActiveShipment: false);

        Assert.Equal(expected, decision.IsAllowed);
    }

    [Fact]
    // Đơn đang giao mà vận đơn còn hiệu lực thì nhân viên chưa hủy được. Lý do trả về là vận đơn, không phải người chuyển.
    public void Evaluate_StaffCancelShippingOrder_WithActiveShipment_IsRejectedBecauseOfShipment()
    {
        var decision = OrderTransitions.Evaluate(
            OrderStatus.Shipping, OrderStatus.Cancelled, TransitionActor.Staff, hasActiveShipment: true);

        Assert.False(decision.IsAllowed);
        Assert.Equal(TransitionRejection.ShipmentStillActive, decision.Rejection);
    }

    [Fact]
    // Vận đơn đã hủy thì nhân viên hủy được đơn `shipping`.
    public void Evaluate_StaffCancelShippingOrder_WithCancelledShipment_IsAllowed()
    {
        var decision = OrderTransitions.Evaluate(
            OrderStatus.Shipping, OrderStatus.Cancelled, TransitionActor.Staff, hasActiveShipment: false);

        Assert.True(decision.IsAllowed);
        Assert.Equal(TransitionRejection.None, decision.Rejection);
    }

    [Fact]
    // Cờ vận đơn chỉ chặn cặp `shipping` sang `cancelled`. Các cặp khác không đổi kết quả theo cờ này.
    public void Evaluate_ActiveShipmentFlag_DoesNotChangeOtherPairs()
    {
        foreach (var from in AllStatuses)
        {
            foreach (var to in AllStatuses)
            {
                foreach (var actor in AllActors)
                {
                    var isShippingCancel = from == OrderStatus.Shipping && to == OrderStatus.Cancelled;
                    if (isShippingCancel)
                    {
                        continue;
                    }

                    var withoutShipment = OrderTransitions.Evaluate(from, to, actor, hasActiveShipment: false).IsAllowed;
                    var withShipment = OrderTransitions.Evaluate(from, to, actor, hasActiveShipment: true).IsAllowed;

                    Assert.Equal(withoutShipment, withShipment);
                }
            }
        }
    }

    [Fact]
    // Khách không bao giờ hủy được đơn `shipping`, dù vận đơn đã hủy. Lý do là người chuyển, không phải vận đơn.
    public void Evaluate_CustomerCancelShippingOrder_IsRejectedBecauseOfActor()
    {
        var decision = OrderTransitions.Evaluate(
            OrderStatus.Shipping, OrderStatus.Cancelled, TransitionActor.Customer, hasActiveShipment: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal(TransitionRejection.ActorNotAllowed, decision.Rejection);
    }

    [Fact]
    // Cặp không có trong bảng trả lý do riêng, để API phân biệt với trường hợp sai người chuyển.
    public void Evaluate_PairOutsideTable_ReportsNotInTable()
    {
        var decision = OrderTransitions.Evaluate(
            OrderStatus.Delivered, OrderStatus.Confirmed, TransitionActor.Staff, hasActiveShipment: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal(TransitionRejection.NotInTransitionTable, decision.Rejection);
    }

    [Fact]
    // Cặp có trong bảng nhưng sai người chuyển trả lý do sai người chuyển.
    public void Evaluate_PairInTableWithWrongActor_ReportsActorNotAllowed()
    {
        var decision = OrderTransitions.Evaluate(
            OrderStatus.PendingConfirmation, OrderStatus.Confirmed, TransitionActor.Customer, hasActiveShipment: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal(TransitionRejection.ActorNotAllowed, decision.Rejection);
    }

    [Fact]
    // Trạng thái cuối `delivered` và `returned` không có đường đi tiếp với bất kỳ ai.
    public void Evaluate_FromDeliveredOrReturned_NoTransitionIsAllowed()
    {
        foreach (var from in new[] { OrderStatus.Delivered, OrderStatus.Returned })
        {
            foreach (var to in AllStatuses)
            {
                foreach (var actor in AllActors)
                {
                    Assert.False(OrderTransitions.Evaluate(from, to, actor, hasActiveShipment: false).IsAllowed);
                }
            }
        }
    }

    [Fact]
    // Chỉ hệ thống đưa được đơn đã hủy về lại `pending_confirmation`, khi tiền về muộn và còn đủ hàng.
    public void Evaluate_RestoreCancelledOrder_OnlySystemMayDoIt()
    {
        Assert.True(OrderTransitions.Evaluate(
            OrderStatus.Cancelled, OrderStatus.PendingConfirmation, TransitionActor.System, hasActiveShipment: false).IsAllowed);
        Assert.False(OrderTransitions.Evaluate(
            OrderStatus.Cancelled, OrderStatus.PendingConfirmation, TransitionActor.Staff, hasActiveShipment: false).IsAllowed);
        Assert.False(OrderTransitions.Evaluate(
            OrderStatus.Cancelled, OrderStatus.PendingConfirmation, TransitionActor.Customer, hasActiveShipment: false).IsAllowed);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled, PaymentStatus.Paid, PaymentStatus.RefundPending)]
    [InlineData(OrderStatus.Returned, PaymentStatus.Paid, PaymentStatus.RefundPending)]
    [InlineData(OrderStatus.Cancelled, PaymentStatus.Unpaid, PaymentStatus.Unpaid)]
    [InlineData(OrderStatus.Returned, PaymentStatus.Unpaid, PaymentStatus.Unpaid)]
    [InlineData(OrderStatus.Cancelled, PaymentStatus.RefundPending, PaymentStatus.RefundPending)]
    [InlineData(OrderStatus.Cancelled, PaymentStatus.Refunded, PaymentStatus.Refunded)]
    [InlineData(OrderStatus.Confirmed, PaymentStatus.Paid, PaymentStatus.Paid)]
    [InlineData(OrderStatus.Delivered, PaymentStatus.Paid, PaymentStatus.Paid)]
    [InlineData(OrderStatus.PendingConfirmation, PaymentStatus.Paid, PaymentStatus.Paid)]
    // Hủy hoặc hoàn đơn đã trả tiền thì thanh toán chờ hoàn tiền. Các trường hợp khác không đổi thanh toán.
    public void PaymentStatusAfterOrderChange_OnlyPaidOrdersThatEndAreMarkedForRefund(
        OrderStatus newOrderStatus, PaymentStatus current, PaymentStatus expected)
    {
        Assert.Equal(expected, OrderTransitions.PaymentStatusAfterOrderChange(newOrderStatus, current));
    }
}
