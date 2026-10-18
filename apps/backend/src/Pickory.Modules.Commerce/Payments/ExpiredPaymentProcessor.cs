using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Pickory.Modules.Catalog.Contracts.Stock;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Payments;

// Mỗi đơn có transaction riêng để lỗi kho không chặn các đơn hết hạn khác.
public sealed class ExpiredPaymentProcessor(
    CommerceDbContext database, IStockService stock, IEventDispatcher dispatcher,
    TimeProvider clock, ILogger<ExpiredPaymentProcessor> logger)
{
    private const int BatchSize = 100;

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // Chỉ lấy ID để lần đọc sau khóa không dùng entity đã theo dõi từ truy vấn chọn đơn.
        var ids = await database.Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatus.PendingPayment && order.PaymentMethod == PaymentMethod.Sepay
                && order.PaymentDeadline <= now)
            .OrderBy(order => order.PaymentDeadline).ThenBy(order => order.Id)
            .Take(BatchSize).Select(order => order.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            // Dừng trước đơn tiếp theo để yêu cầu tắt worker không mở thêm transaction.
            cancellationToken.ThrowIfCancellationRequested();
            var events = new DomainEventCollector(dispatcher);
            try
            {
                // Transaction riêng giữ trạng thái, lịch sử và hàng trả cùng commit hoặc rollback.
                await ExpireAsync(id, now, events, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Entity và sự kiện lỗi không được đi vào transaction của đơn tiếp theo.
                events.Discard();
                database.ChangeTracker.Clear();
                // Log ID đơn giúp tìm lần hủy lỗi mà không ghi thông tin liên hệ khách.
                logger.LogError(exception, "Hủy đơn hết hạn {OrderId} thất bại.", id);
                continue;
            }
            // Handler chỉ đọc dữ liệu đã commit, không mất sự kiện khi worker bị dừng sau commit.
            await events.FlushAsync(CancellationToken.None);
            // Vòng tiếp theo phải đọc lại trạng thái mới nhất, kể cả khi dùng cùng processor.
            database.ChangeTracker.Clear();
        }
    }

    private async Task ExpireAsync(string id, DateTime now, DomainEventCollector events, CancellationToken cancellationToken)
    {
        // Transaction sở hữu cả thay đổi đơn và lệnh kho, không cho hai module commit riêng.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Khóa cùng dòng mà webhook dùng để hai bên không cùng đổi trạng thái đơn.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "id" FROM orders WHERE "id" = {id} FOR UPDATE""", cancellationToken);
        // Đọc sau khóa để thấy thanh toán mà webhook vừa commit trong lúc job đợi.
        var order = await database.Orders.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        // Webhook hoặc vòng khác có thể đã đổi đơn sau truy vấn chọn ID.
        if (order is null || order.Status != OrderStatus.PendingPayment || order.PaymentMethod != PaymentMethod.Sepay
            || order.PaymentDeadline is null || order.PaymentDeadline > now)
        {
            return;
        }
        // Máy trạng thái kiểm quyền hệ thống trước khi trả kho và ghi lý do hủy.
        if (!OrderTransitions.Evaluate(order.Status, OrderStatus.Cancelled, TransitionActor.System, false).IsAllowed)
        {
            throw new InvalidOperationException("Máy trạng thái từ chối hủy đơn hết hạn.");
        }
        // Catalog dùng transaction của đơn, nên lỗi ghi đơn cũng hoàn lại hàng vừa trả.
        await stock.ReleaseAsync(id, new StockCallerTransaction(transaction.GetDbTransaction(), events), cancellationToken);
        var previousStatus = order.Status;
        // Chỉ đổi trạng thái sau khi kho đã trả thành công trong transaction hiện tại.
        order.Status = OrderStatus.Cancelled;
        // Cờ phân biệt hết hạn với khách tự hủy, không cho webhook khôi phục nhầm đơn.
        if (!order.Flags.Contains(OrderReportMarkers.PaymentExpiredFlag))
        {
            order.Flags.Add(OrderReportMarkers.PaymentExpiredFlag);
        }
        // Lịch sử cùng commit với cờ để webhook phân biệt đúng nguyên nhân đơn bị hủy.
        database.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = id, FromStatus = previousStatus, ToStatus = order.Status,
            Reason = "Quá hạn thanh toán.", CreatedAt = now,
        });
        // Gom trước commit, nhưng chỉ phát khi toàn bộ trạng thái và tồn kho đã lưu.
        events.Emit(new OrderStatusChangedEvent(id, order.Code, previousStatus, order.Status));
        // Lưu trạng thái và lịch sử trước khi commit để lỗi ghi DB hoàn lại cả hàng trả.
        await database.SaveChangesAsync(cancellationToken);
        // Commit là ranh giới bắt buộc trước khi vòng ngoài phát sự kiện.
        await transaction.CommitAsync(cancellationToken);
    }
}
