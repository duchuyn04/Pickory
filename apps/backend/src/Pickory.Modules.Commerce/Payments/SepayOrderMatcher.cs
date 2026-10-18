using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Payments;

// Luật khớp một giao dịch SePay vào một đơn đã biết, dùng chung cho webhook và route gắn tay của nhân viên.
// Bên gọi đã mở transaction, đã khóa dòng đơn và đã nạp đơn.
internal sealed class SepayOrderMatcher(CommerceDbContext database, TimeProvider clock, SepayLatePaymentMatcher latePayments)
{
    private const string WebhookPaidReason = "SePay đã nhận đủ tiền.";
    private const string StaffAttachedReason = "Nhân viên gắn giao dịch SePay vào đơn.";

    // Trả `false` khi đơn không nhận được tiền, và không đổi gì.
    // `actorId` là `null` khi webhook tự khớp, và là mã nhân viên khi gắn tay.
    public async Task<bool> TryMatchAsync(
        Order order, PaymentTransaction payment, DomainEventCollector events, string? actorId, CancellationToken cancellationToken)
    {
        // Chỉ đơn hết hạn chưa trả tiền được giữ lại hàng, không khôi phục đơn khách tự hủy.
        if (order.Status == OrderStatus.Cancelled && order.Flags.Contains(OrderReportMarkers.PaymentExpiredFlag)
            && order.PaymentMethod == PaymentMethod.Sepay && order.PaymentStatus == PaymentStatus.Unpaid)
        {
            // Chỉ khôi phục cần dòng hàng, nên chứng từ thường không tải snapshot sản phẩm thừa.
            var itemsEntry = database.Entry(order).Collection(row => row.Items);
            if (!itemsEntry.IsLoaded)
            {
                await itemsEntry.LoadAsync(cancellationToken);
            }
            await latePayments.MatchAsync(order, payment, events, actorId, cancellationToken);
            return true;
        }
        // Đơn COD, đã trả hoặc hủy vì lý do khác không nhận được tiền SePay.
        if (order.Status != OrderStatus.PendingPayment || order.PaymentMethod != PaymentMethod.Sepay || order.PaymentStatus != PaymentStatus.Unpaid)
        {
            return false;
        }
        // Chỉ dòng `matched` được gắn `OrderId`, hai trường phải commit cùng nhau.
        payment.MatchStatus = PaymentMatchStatuses.Matched;
        payment.OrderId = order.Id;
        // So từng giao dịch với tổng đơn, không cộng dồn nhiều lần chuyển thiếu tiền.
        if (payment.Amount >= order.Total)
        {
            // Bước chuyển sang `pending_confirmation` chỉ dành cho hệ thống. Nhân viên gắn tay chỉ xác nhận chứng từ, nên luật vẫn xét theo hệ thống.
            var decision = OrderTransitions.Evaluate(order.Status, OrderStatus.PendingConfirmation, TransitionActor.System, hasActiveShipment: false);
            // Máy trạng thái là nguồn luật duy nhất cho bước đổi trạng thái đơn.
            if (!decision.IsAllowed)
            {
                throw new InvalidOperationException("Máy trạng thái từ chối ghi nhận thanh toán.");
            }
            var previousStatus = order.Status;
            order.PaymentStatus = PaymentStatus.Paid;
            order.Status = OrderStatus.PendingConfirmation;
            // Lịch sử và trạng thái phải cùng commit để người đối soát thấy nguyên nhân chuyển đơn.
            // Gắn tay ghi mã nhân viên và lý do riêng để phân biệt với webhook.
            database.OrderStatusHistory.Add(new OrderStatusHistory
            {
                OrderId = order.Id, FromStatus = previousStatus, ToStatus = order.Status,
                ActorId = actorId, Reason = actorId is null ? WebhookPaidReason : StaffAttachedReason,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            });
            // Tiền thừa cần cờ đối soát, không đổi số tiền phải trả của đơn.
            if (payment.Amount > order.Total)
            {
                AddFlag(order, "overpaid");
            }
            // Collector hoãn sự kiện đến sau commit, không gọi handler trong transaction.
            events.Emit(new OrderPaidEvent(order.Id, order.Code, order.UserId, order.AnonymousId, payment.Amount));
            return true;
        }
        // Chuyển thiếu giữ đơn chưa trả, chỉ gắn cờ để nhân viên đối soát.
        AddFlag(order, "underpaid");
        return true;
    }

    private static void AddFlag(Order order, string flag)
    {
        // Các giao dịch khác nhau có thể cùng thiếu tiền, nhưng cờ chỉ xuất hiện một lần.
        if (!order.Flags.Contains(flag))
        {
            order.Flags.Add(flag);
        }
    }
}
