using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Payments;

// Bên gọi đã khóa đơn; giữ lại kho dùng đúng transaction đang khớp chứng từ.
internal sealed class SepayLatePaymentMatcher(CommerceDbContext database, IStockService stock, TimeProvider clock)
{
    public async Task MatchAsync(
        Order order, PaymentTransaction payment, DomainEventCollector events, string? actorId, CancellationToken cancellationToken)
    {
        // Tiền về muộn vẫn phải gắn chứng từ cho đơn kể cả khi cần hoàn tiền.
        payment.MatchStatus = PaymentMatchStatuses.Matched;
        payment.OrderId = order.Id;
        // Chuyển thiếu không cộng dồn, vì mỗi chứng từ phải đủ tiền để khôi phục đơn.
        if (payment.Amount < order.Total)
        {
            // Tiền thiếu không đủ khôi phục đơn; cờ giúp nhân viên xác định lý do hoàn tiền.
            SetPaymentStatus(order, PaymentStatus.RefundPending);
            AddFlag(order, "underpaid");
            return;
        }
        // Chỉ giữ lại khi tiền đủ; Catalog báo thiếu hàng mà không trừ một phần đơn.
        var items = order.Items.Select(item => new StockItem(item.SkuId, item.Quantity)).ToList();
        var reservation = await stock.ReserveAsync(order.Id, items,
            new StockCallerTransaction(database.Database.CurrentTransaction!.GetDbTransaction(), events), cancellationToken);
        if (!reservation.Ok)
        {
            // Hàng đã bán cho đơn khác thì giữ đơn hủy, không làm tồn kho âm.
            SetPaymentStatus(order, PaymentStatus.RefundPending);
            return;
        }
        // Chỉ hệ thống được khôi phục đơn đã hủy; lỗi luật phải rollback cả hàng vừa giữ.
        if (!OrderTransitions.Evaluate(order.Status, OrderStatus.PendingConfirmation, TransitionActor.System, false).IsAllowed)
        {
            throw new InvalidOperationException("Máy trạng thái từ chối khôi phục đơn hết hạn.");
        }
        // Tiền đủ và kho đã giữ thành công mới cho phép đánh dấu đơn đã trả.
        SetPaymentStatus(order, PaymentStatus.Paid);
        var previousStatus = order.Status;
        // Chỉ chuyển đơn sau khi cả luật thanh toán và luật đơn đã chấp nhận.
        order.Status = OrderStatus.PendingConfirmation;
        // Bỏ dấu hết hạn để lần hủy sau không bị coi là lần hết hạn còn có thể khôi phục.
        order.Flags.Remove(OrderReportMarkers.PaymentExpiredFlag);
        AddFlag(order, "late_payment");
        if (payment.Amount > order.Total)
        {
            // Tiền thừa cần đối soát, không đổi tổng tiền đã chốt khi đặt đơn.
            AddFlag(order, "overpaid");
        }
        // Lịch sử khôi phục phải commit cùng trạng thái và movement giữ lại hàng. `actorId` chỉ có khi nhân viên gắn tay.
        database.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id, FromStatus = previousStatus, ToStatus = order.Status, ActorId = actorId,
            Reason = "SePay nhận đủ tiền sau khi hết hạn.", CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
        // Collector của transaction khớp hoãn cả hai sự kiện đến sau commit.
        events.Emit(new OrderStatusChangedEvent(order.Id, order.Code, previousStatus, order.Status));
        events.Emit(new OrderPaidEvent(order.Id, order.Code, order.UserId, order.AnonymousId, payment.Amount));
    }

    private static void SetPaymentStatus(Order order, PaymentStatus target)
    {
        // Luật thanh toán phải cho phép hệ thống đổi trạng thái trước khi ghi giá trị mới.
        if (!PaymentTransitions.Evaluate(order.PaymentStatus, target, TransitionActor.System).IsAllowed)
        {
            throw new InvalidOperationException("Máy trạng thái từ chối ghi nhận tiền về muộn.");
        }
        order.PaymentStatus = target;
    }

    private static void AddFlag(Order order, string flag)
    {
        // Cờ lưu theo tập giá trị để giao dịch khác không nhân đôi dấu đối soát.
        if (!order.Flags.Contains(flag))
        {
            order.Flags.Add(flag);
        }
    }
}
