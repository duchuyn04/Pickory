using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Modules.Shipping.Contracts.Events;
using Pickory.Modules.Shipping.Contracts.Shipments;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Orders;

// Đưa đơn sang `delivered`, `returned` hoặc về `confirmed` khi vận đơn của đơn báo đã giao, đã hoàn hoặc bị hãng vận chuyển hủy.
// Người gọi là hệ thống, nên lịch sử có `actorId` null và không ghi audit.
public sealed class ShipmentStatusChangedHandler(
    CommerceDbContext database,
    IShippingService shipping,
    IEventDispatcher dispatcher,
    TimeProvider clock,
    ILogger<ShipmentStatusChangedHandler> logger) : IDomainEventHandler<ShipmentStatusChangedEvent>
{
    // Cờ báo nhân viên cần kiểm tra đơn vì hãng vận chuyển đã hủy vận đơn. Tạo vận đơn mới thì bỏ cờ.
    public const string ShipmentCancelledFlag = "shipment_cancelled";

    private const string DeliveredReason = "Vận đơn báo đã giao hàng.";
    private const string ReturnedReason = "Vận đơn báo đã hoàn hàng về shop.";
    private const string CarrierCancelledReason = "Hãng vận chuyển đã hủy vận đơn. Đơn về trạng thái đã xác nhận để nhân viên kiểm tra.";

    // Thay đổi đơn mà sự kiện yêu cầu. `IsCarrierCancellation` đổi cách xác định vận đơn hiện tại và thêm cờ.
    private sealed record PlannedChange(OrderStatus Target, string Reason, bool IsCarrierCancellation);

    public async Task HandleAsync(ShipmentStatusChangedEvent @event, CancellationToken cancellationToken = default)
    {
        var plannedChange = PlanChange(@event);
        if (plannedChange is null)
        {
            return;
        }

        var target = plannedChange.Target;

        // `Ref` là mã đơn. Vận đơn do kênh khác tạo không có đơn tương ứng, nên bỏ qua.
        var orderId = await database.Orders.AsNoTracking()
            .Where(order => order.Code == @event.Ref)
            .Select(order => order.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (orderId is null)
        {
            logger.LogInformation("Bỏ qua sự kiện vận đơn {ShipmentId} vì không có đơn mã {Ref}.", @event.ShipmentId, @event.Ref);
            return;
        }

        // Chỉ vận đơn hiện tại của đơn mới đổi được đơn. Sự kiện muộn của vận đơn đã hủy mang `ShipmentId` khác.
        var currentShipment = await shipping.GetShipmentByRefAsync(@event.Ref, cancellationToken);
        if (!IsEventForCurrentShipment(plannedChange, @event, currentShipment))
        {
            logger.LogInformation(
                "Bỏ qua sự kiện vận đơn {ShipmentId} vì không phải vận đơn hiện tại của đơn {Ref}.", @event.ShipmentId, @event.Ref);
            return;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var order = await OrderRowLock.LoadForUpdateAsync(database, orderId, cancellationToken);

        // Đơn đã ở trạng thái đích thì sự kiện đến lần hai. Bỏ qua để không ghi lịch sử và không phát sự kiện lần hai.
        if (order.Status == target)
        {
            return;
        }

        // Cả ba thay đổi chỉ hợp lệ khi đơn đang `shipping`. Đơn ở trạng thái khác giữ nguyên, kể cả khi bảng trạng thái cho phép cặp này.
        var decision = OrderTransitions.Evaluate(order.Status, target, TransitionActor.System, hasActiveShipment: false);
        if (order.Status != OrderStatus.Shipping || !decision.IsAllowed)
        {
            logger.LogWarning(
                "Bỏ qua sự kiện vận đơn {ShipmentId} vì đơn {Ref} ở trạng thái {OrderStatus} không chuyển được sang {TargetStatus}.",
                @event.ShipmentId,
                @event.Ref,
                order.Status,
                target);
            return;
        }

        var events = new DomainEventCollector(dispatcher);
        try
        {
            var previousStatus = order.Status;
            order.Status = target;

            // Đơn đã trả tiền mà hoàn về thì chuyển sang chờ hoàn tiền. Hàng hoàn không tự nhập kho, vì cần nhân viên kiểm.
            order.PaymentStatus = OrderTransitions.PaymentStatusAfterOrderChange(target, order.PaymentStatus);

            // Cờ báo đơn cần nhân viên kiểm tra. Đơn chỉ vào nhánh này một lần vì đơn đã `confirmed` bị chặn ở bước trên.
            if (plannedChange.IsCarrierCancellation && !order.Flags.Contains(ShipmentCancelledFlag))
            {
                order.Flags.Add(ShipmentCancelledFlag);
            }

            var now = OrderHistoryEntries.TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);
            OrderHistoryEntries.Add(database, order, previousStatus, target, actorId: null, plannedChange.Reason, now);

            // Gom sự kiện để phát sau commit. Bên nghe chỉ đọc dữ liệu đã lưu.
            events.Emit(new OrderStatusChangedEvent(order.Id, order.Code, previousStatus, target));

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            events.Discard();
            throw;
        }

        await events.FlushAsync(cancellationToken);
    }

    // Trả null khi sự kiện không đổi đơn. Các trạng thái giao khác chỉ là tiến trình của vận đơn.
    private static PlannedChange? PlanChange(ShipmentStatusChangedEvent @event)
    {
        switch (@event.Status)
        {
            case ShipmentStatus.Delivered:
                return new PlannedChange(OrderStatus.Delivered, DeliveredReason, IsCarrierCancellation: false);
            case ShipmentStatus.Returned:
                return new PlannedChange(OrderStatus.Returned, ReturnedReason, IsCarrierCancellation: false);
            case ShipmentStatus.Cancelled when @event.Source == ShipmentStatusChangeSource.Carrier:
                // Luồng hủy vận đơn của nhân viên đã ghi lịch sử có `actorId` và audit cho lần hủy này. Handler bỏ qua để không ghi lần hai.
                return new PlannedChange(OrderStatus.Confirmed, CarrierCancelledReason, IsCarrierCancellation: true);
            default:
                return null;
        }
    }

    // Vận đơn đã hủy bị đổi `Ref`, nên đơn không còn vận đơn hiệu lực nào khi chính vận đơn cuối của đơn bị hủy.
    // Còn vận đơn hiệu lực khác thì sự kiện hủy là sự kiện muộn của vận đơn cũ.
    private static bool IsEventForCurrentShipment(
        PlannedChange plannedChange, ShipmentStatusChangedEvent @event, Shipment? currentShipment)
    {
        if (plannedChange.IsCarrierCancellation)
        {
            return currentShipment is null;
        }

        return currentShipment is not null && currentShipment.Id == @event.ShipmentId;
    }
}
