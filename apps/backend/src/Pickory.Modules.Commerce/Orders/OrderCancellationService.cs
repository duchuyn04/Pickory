using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Orders;

// Người yêu cầu hủy. `ActorId` là `null` với khách vãng lai.
public sealed record OrderCancellationRequest(TransitionActor Actor, string? ActorId, string Reason);

// Phần hủy đơn dùng chung cho khách tự hủy và nhân viên hủy. Hai bên khác nhau ở người gọi và kiểm vận đơn.
public sealed class OrderCancellationService(
    CommerceDbContext database,
    IStockService stock,
    IShippingService shipping,
    IAuditLogWriter auditLogWriter,
    IEventDispatcher dispatcher,
    TimeProvider clock)
{
    public const string AuditAction = "commerce.order.cancel";
    public const string AuditTargetType = "order";

    private const string InvalidTransitionCode = "INVALID_ORDER_TRANSITION";
    private const string InvalidTransitionMessage = "Không thể hủy đơn ở trạng thái này.";
    private const string ShipmentActiveMessage = "Đơn đang giao còn vận đơn hiệu lực. Hủy vận đơn trước khi hủy đơn.";

    public async Task<Order> CancelAsync(
        string orderId, OrderCancellationRequest request, CancellationToken cancellationToken)
    {
        // Mở transaction để hoàn trả tồn kho, đổi trạng thái và ghi audit cùng thành công hoặc cùng quay lui.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Khóa dòng đơn rồi đọc lại. Hai yêu cầu hủy cùng đơn xếp hàng, và yêu cầu sau thấy đơn đã hủy.
        var order = await OrderRowLock.LoadForUpdateAsync(database, orderId, cancellationToken);

        // Chỉ nhân viên hủy được đơn đang giao, và chỉ khi vận đơn không còn hiệu lực. Tra sau khi giữ khóa.
        var hasActiveShipment = false;
        if (request.Actor == TransitionActor.Staff && order.Status == OrderStatus.Shipping)
        {
            hasActiveShipment = await shipping.GetShipmentByRefAsync(order.Code, cancellationToken) is not null;
        }

        // Máy trạng thái quyết định ai được hủy ở trạng thái nào. Lý do từ chối chọn câu trả lời cho người dùng.
        var decision = OrderTransitions.Evaluate(order.Status, OrderStatus.Cancelled, request.Actor, hasActiveShipment);
        if (!decision.IsAllowed)
        {
            var message = decision.Rejection == TransitionRejection.ShipmentStillActive
                ? ShipmentActiveMessage
                : InvalidTransitionMessage;
            throw new AppException(InvalidTransitionCode, 409, message);
        }

        var events = new DomainEventCollector(dispatcher);
        try
        {
            // Trả lại hàng đã giữ cho kho trong cùng transaction của đơn.
            await stock.ReleaseAsync(
                order.Id,
                new StockCallerTransaction(transaction.GetDbTransaction(), events),
                cancellationToken);

            var previousStatus = order.Status;
            var previousPaymentStatus = order.PaymentStatus;

            // Đơn đã trả tiền mà bị hủy thì thanh toán sang chờ hoàn tiền.
            order.Status = OrderStatus.Cancelled;
            order.PaymentStatus = OrderTransitions.PaymentStatusAfterOrderChange(OrderStatus.Cancelled, order.PaymentStatus);

            var now = OrderHistoryEntries.TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);
            OrderHistoryEntries.Add(
                database, order, previousStatus, OrderStatus.Cancelled, request.ActorId, request.Reason, now);

            // Gom sự kiện để phát sau commit. Bên nghe chỉ đọc dữ liệu đã lưu.
            events.Emit(new OrderStatusChangedEvent(order.Id, order.Code, previousStatus, OrderStatus.Cancelled));

            await database.SaveChangesAsync(cancellationToken);

            // Mỗi lần nhân viên hủy ghi một dòng audit trong cùng transaction. Không ghi thông tin liên hệ của khách.
            if (request.Actor == TransitionActor.Staff)
            {
                var entry = new AuditLogEntry(
                    request.ActorId!,
                    AuditAction,
                    AuditTargetType,
                    order.Code,
                    new { status = OrderLabels.ToLabel(previousStatus), paymentStatus = OrderLabels.ToLabel(previousPaymentStatus) },
                    new
                    {
                        status = OrderLabels.ToLabel(order.Status),
                        paymentStatus = OrderLabels.ToLabel(order.PaymentStatus),
                        reason = request.Reason,
                    });
                await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Hủy toàn bộ sự kiện đã gom khi transaction gặp lỗi.
            events.Discard();
            throw;
        }

        // Phát các sự kiện miền ra bên ngoài sau khi transaction đã commit thành công.
        await events.FlushAsync(cancellationToken);
        return order;
    }
}
