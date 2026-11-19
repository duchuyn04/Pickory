using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Shipping.Contracts;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Nhân viên đổi trạng thái giao hàng của đơn bằng tay khi luồng GHN không dùng được.
// Service chỉ hỏi vận đơn GHN còn hiệu lực, không gọi nghiệp vụ GHN.
public sealed class AdminOrderFulfillmentService(
    CommerceDbContext database,
    IShippingService shipping,
    AdminOrderService orders,
    IAuditLogWriter auditLogWriter,
    IEventDispatcher dispatcher,
    TimeProvider clock)
{
    public const string FulfillmentAuditAction = "commerce.order.fulfillment_update";
    public const string ManualShippingFlag = "manual_shipping";

    private const string OrderNotFoundMessage = "Không tìm thấy đơn hàng.";
    private const string InvalidTransitionCode = "INVALID_ORDER_TRANSITION";
    private const string InvalidTransitionMessage = "Không thể chuyển trạng thái giao hàng theo yêu cầu.";
    private const string ShipmentActiveMessage = "Đơn còn vận đơn GHN hiệu lực. Hủy vận đơn trước khi giao thủ công.";

    // Chuyển đổi nhân viên được làm tay, cùng điều kiện vận đơn và cách đổi cờ của chúng.
    private sealed record Transition(
        OrderStatus From,
        OrderStatus To,
        bool RequiresNoActiveShipment,
        Action<List<string>> ApplyFlags);

    // Bảng chuyển đổi hiện chỉ nhận cặp `confirmed` sang `shipping`. Cặp khác bị từ chối cho tới khi có dòng mới ở đây.
    private static readonly Transition[] SupportedTransitions =
    [
        new(OrderStatus.Confirmed, OrderStatus.Shipping, RequiresNoActiveShipment: true, flags => AddFlag(flags, ManualShippingFlag)),
    ];

    public async Task<AdminOrderDetailResponse> UpdateAsync(
        string code, string actorId, OrderStatus toStatus, string reason, CancellationToken cancellationToken)
    {
        var orderId = await FindOrderIdAsync(code, cancellationToken);

        // Transaction giữ thay đổi đơn, lịch sử và audit cùng thành công hoặc cùng quay lui.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var order = await OrderRowLock.LoadForUpdateAsync(database, orderId, cancellationToken);

        // PUT có tính lặp lại an toàn. Đơn đã ở trạng thái đích thì trả 200 và không ghi lịch sử, audit, sự kiện hay cờ.
        if (order.Status == toStatus)
        {
            return await orders.GetDetailAsync(code, cancellationToken);
        }

        // Máy trạng thái là nguồn luật về cặp được phép. Bảng này chỉ thu hẹp thêm theo các cặp đã mở cho nhân viên.
        var decision = OrderTransitions.Evaluate(order.Status, toStatus, TransitionActor.Staff, hasActiveShipment: false);
        var transition = SupportedTransitions.FirstOrDefault(candidate => candidate.From == order.Status && candidate.To == toStatus);
        if (!decision.IsAllowed || transition is null)
        {
            throw new AppException(InvalidTransitionCode, 409, InvalidTransitionMessage);
        }

        // Vận đơn GHN còn hiệu lực nghĩa là GHN đang giữ hàng. Tra sau khi giữ khóa để thấy trạng thái mới nhất.
        if (transition.RequiresNoActiveShipment
            && await shipping.GetShipmentByRefAsync(order.Code, cancellationToken) is not null)
        {
            throw new AppException(InvalidTransitionCode, 409, ShipmentActiveMessage);
        }

        var events = new DomainEventCollector(dispatcher);
        try
        {
            // Chụp trạng thái cũ trước khi sửa. Lịch sử và sự kiện cần giá trị này.
            var previousStatus = order.Status;
            // Chụp bản sao cờ trước khi sửa. Audit `before` không đổi theo cờ mới.
            var previousFlags = order.Flags.ToArray();

            // Trạng thái đích lấy từ dòng của bảng. Cặp chuyển đổi này đã qua kiểm tra ở trên.
            order.Status = transition.To;
            // Cờ đổi độc lập với trạng thái. Mỗi chuyển đổi tự quy định cờ nào thêm hoặc bỏ.
            transition.ApplyFlags(order.Flags);

            // Lịch sử giữ lý do của nhân viên nguyên văn, không thêm tiền tố.
            var now = OrderHistoryEntries.TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);
            OrderHistoryEntries.Add(database, order, previousStatus, transition.To, actorId, reason, now);

            // Gom sự kiện để phát sau commit. Bên nghe chỉ đọc dữ liệu đã lưu.
            events.Emit(new OrderStatusChangedEvent(order.Id, order.Code, previousStatus, transition.To));

            await database.SaveChangesAsync(cancellationToken);

            // Mỗi lần đổi ghi một dòng audit trong cùng transaction. Không ghi thông tin liên hệ của khách.
            var entry = new AuditLogEntry(
                actorId,
                FulfillmentAuditAction,
                OrderCancellationService.AuditTargetType,
                order.Code,
                new { status = OrderLabels.ToLabel(previousStatus), flags = previousFlags },
                new { status = OrderLabels.ToLabel(order.Status), flags = order.Flags.ToArray(), reason });
            await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Giao dịch quay lui thì không phát sự kiện của thay đổi đã hủy.
            events.Discard();
            throw;
        }

        // Handler chỉ thấy dữ liệu đã commit.
        await events.FlushAsync(cancellationToken);
        return await orders.GetDetailAsync(code, cancellationToken);
    }

    // Không thêm cờ trùng, để cờ không lặp khi dữ liệu cũ đã có sẵn.
    private static void AddFlag(List<string> flags, string flag)
    {
        if (!flags.Contains(flag))
        {
            flags.Add(flag);
        }
    }

    // Tra mã đơn sang `Id` trước khi mở transaction, để mã không tồn tại trả 404 mà không giữ khóa hay kết nối.
    private async Task<string> FindOrderIdAsync(string code, CancellationToken cancellationToken)
    {
        var orderId = await database.Orders.AsNoTracking()
            .Where(order => order.Code == code)
            .Select(order => order.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (orderId is null)
        {
            throw new AppException("ORDER_NOT_FOUND", 404, OrderNotFoundMessage);
        }

        return orderId;
    }
}
