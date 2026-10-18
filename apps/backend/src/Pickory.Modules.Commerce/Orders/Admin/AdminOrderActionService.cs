using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Orders.Admin;

// Thao tác ghi của nhân viên lên đơn: xác nhận, hủy, xuất hóa đơn, nhận tiền COD và hoàn tiền.
// Mỗi thao tác chạy trong một transaction, khóa dòng đơn, ghi lịch sử và audit cùng commit.
public sealed class AdminOrderActionService(
    CommerceDbContext database,
    OrderCancellationService cancellation,
    AdminOrderService orders,
    IAuditLogWriter auditLogWriter,
    IEventDispatcher dispatcher,
    TimeProvider clock)
{
    public const string ConfirmAuditAction = "commerce.order.confirm";
    public const string InvoiceIssueAuditAction = "commerce.order.invoice_issue";
    public const string InvoiceIssuedFlag = "invoice_issued";
    public const string CodReceiptAuditAction = "commerce.order.cod_receipt";
    public const string RefundAuditAction = "commerce.order.refund";

    private const string OrderNotFoundMessage = "Không tìm thấy đơn hàng.";
    private const string InvalidTransitionCode = "INVALID_ORDER_TRANSITION";
    private const string ConfirmRejectedMessage = "Chỉ xác nhận được đơn đang chờ xác nhận.";
    private const string InvoiceNotRequestedMessage = "Đơn này không có thông tin hóa đơn VAT để đánh dấu.";
    private const string InvoiceOrderEndedMessage = "Đơn đã hủy hoặc đã hoàn nên không đánh dấu xuất hóa đơn.";
    private const string InvoiceIssuedReason = "Đã xuất hóa đơn VAT.";
    private const string CodReceiptNotCodMessage = "Chỉ đơn COD mới nhận tiền COD từ GHN.";
    private const string CodReceiptNotDeliveredMessage = "Chỉ nhận tiền COD của đơn đã giao.";
    private const string CodReceiptPaymentStateMessage = "Thanh toán của đơn không còn ở trạng thái chưa trả.";
    private const string RefundRejectedMessage = "Chỉ đánh dấu hoàn tiền cho đơn đang chờ hoàn tiền.";
    private const string RefundDifferentRefMessage = "Đơn đã hoàn tiền với một mã giao dịch khác.";

    public async Task<AdminOrderDetailResponse> ConfirmAsync(
        string code, string actorId, CancellationToken cancellationToken)
    {
        var orderId = await FindOrderIdAsync(code, cancellationToken);

        // Transaction giữ thay đổi đơn, lịch sử và audit cùng thành công hoặc cùng quay lui.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var order = await OrderRowLock.LoadForUpdateAsync(database, orderId, cancellationToken);

        // Bảng trạng thái còn cho nhân viên đưa `shipping` về `confirmed` khi hủy vận đơn, nên xác nhận phải tự chặn nguồn đó.
        // Kiểm sau khi giữ khóa để thấy trạng thái mới nhất.
        var decision = OrderTransitions.Evaluate(order.Status, OrderStatus.Confirmed, TransitionActor.Staff, hasActiveShipment: false);
        if (order.Status != OrderStatus.PendingConfirmation || !decision.IsAllowed)
        {
            throw new AppException(InvalidTransitionCode, 409, ConfirmRejectedMessage);
        }

        var events = new DomainEventCollector(dispatcher);
        try
        {
            var previousStatus = order.Status;
            order.Status = OrderStatus.Confirmed;

            var now = OrderHistoryEntries.TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);
            OrderHistoryEntries.Add(
                database, order, previousStatus, OrderStatus.Confirmed, actorId, "Nhân viên xác nhận đơn.", now);

            // Gom sự kiện để phát sau commit. Bên nghe chỉ đọc dữ liệu đã lưu.
            events.Emit(new OrderStatusChangedEvent(order.Id, order.Code, previousStatus, OrderStatus.Confirmed));

            await database.SaveChangesAsync(cancellationToken);

            // Mỗi lần xác nhận ghi một dòng audit trong cùng transaction. Không ghi thông tin liên hệ của khách.
            var entry = new AuditLogEntry(
                actorId,
                ConfirmAuditAction,
                OrderCancellationService.AuditTargetType,
                order.Code,
                new { status = OrderLabels.ToLabel(previousStatus) },
                new { status = OrderLabels.ToLabel(order.Status) });
            await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            events.Discard();
            throw;
        }

        await events.FlushAsync(cancellationToken);
        return await orders.GetDetailAsync(code, cancellationToken);
    }

    public async Task<AdminOrderDetailResponse> CancelAsync(
        string code, string actorId, string reason, CancellationToken cancellationToken)
    {
        var orderId = await FindOrderIdAsync(code, cancellationToken);

        // Phần hủy dùng chung với khách tự hủy: khóa đơn, trả kho, đổi trạng thái, lịch sử, audit và sự kiện.
        await cancellation.CancelAsync(
            orderId, new OrderCancellationRequest(TransitionActor.Staff, actorId, reason), cancellationToken);

        return await orders.GetDetailAsync(code, cancellationToken);
    }

    public async Task<AdminOrderDetailResponse> IssueInvoiceAsync(
        string code, string actorId, CancellationToken cancellationToken)
    {
        var orderId = await FindOrderIdAsync(code, cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var order = await OrderRowLock.LoadForUpdateAsync(database, orderId, cancellationToken);

        // Không có thông tin hóa đơn thì khách chưa xin hóa đơn VAT, nên không có gì để đánh dấu.
        if (order.InvoiceInfo is null)
        {
            throw new AppException("INVOICE_NOT_REQUESTED", 409, InvoiceNotRequestedMessage);
        }

        // Đơn đã hủy hoặc đã hoàn không còn doanh thu để xuất hóa đơn.
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Returned)
        {
            throw new AppException(InvalidTransitionCode, 409, InvoiceOrderEndedMessage);
        }

        // PUT có tính lặp lại an toàn: đã có cờ thì trả 200 và không ghi thêm lịch sử hay audit.
        if (order.Flags.Contains(InvoiceIssuedFlag))
        {
            return await orders.GetDetailAsync(code, cancellationToken);
        }

        order.Flags.Add(InvoiceIssuedFlag);

        // Trạng thái không đổi nên `fromStatus` bằng `toStatus`, và không phát `OrderStatusChangedEvent`.
        var now = OrderHistoryEntries.TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);
        OrderHistoryEntries.Add(database, order, order.Status, order.Status, actorId, InvoiceIssuedReason, now);

        await database.SaveChangesAsync(cancellationToken);

        // Mỗi lần đánh dấu đầu tiên ghi một dòng audit trong cùng transaction.
        var entry = new AuditLogEntry(
            actorId,
            InvoiceIssueAuditAction,
            OrderCancellationService.AuditTargetType,
            order.Code,
            new { invoiceIssued = false },
            new { invoiceIssued = true });
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await orders.GetDetailAsync(code, cancellationToken);
    }

    public async Task<AdminOrderDetailResponse> ReceiveCodAsync(
        string code, string actorId, CancellationToken cancellationToken)
    {
        var orderId = await FindOrderIdAsync(code, cancellationToken);

        // Transaction giữ thay đổi đơn, lịch sử và audit cùng thành công hoặc cùng quay lui.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var order = await OrderRowLock.LoadForUpdateAsync(database, orderId, cancellationToken);

        // Chỉ đơn COD mới có tiền do GHN thu hộ. Đơn SePay nhận tiền qua webhook hoặc khớp tay.
        if (order.PaymentMethod != PaymentMethod.Cod)
        {
            throw new AppException(InvalidTransitionCode, 409, CodReceiptNotCodMessage);
        }

        // Tiền COD chỉ về sau khi giao xong. Đơn chưa giao, đã hủy hoặc đã hoàn chưa có tiền để nhận.
        if (order.Status != OrderStatus.Delivered)
        {
            throw new AppException(InvalidTransitionCode, 409, CodReceiptNotDeliveredMessage);
        }

        // PUT có tính lặp lại an toàn: đơn đã `paid` trả 200 và không ghi thêm lịch sử, audit hay sự kiện.
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            return await orders.GetDetailAsync(code, cancellationToken);
        }

        // Máy trạng thái thanh toán là nguồn luật duy nhất. Nó chặn đơn đang chờ hoàn hoặc đã hoàn.
        var decision = PaymentTransitions.Evaluate(order.PaymentStatus, PaymentStatus.Paid, TransitionActor.Staff);
        if (!decision.IsAllowed)
        {
            throw new AppException(InvalidTransitionCode, 409, CodReceiptPaymentStateMessage);
        }

        var events = new DomainEventCollector(dispatcher);
        try
        {
            var previousPaymentStatus = order.PaymentStatus;
            order.PaymentStatus = PaymentStatus.Paid;

            // Trạng thái đơn không đổi nên `fromStatus` bằng `toStatus`, và không phát `OrderStatusChangedEvent`.
            var now = OrderHistoryEntries.TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);
            OrderHistoryEntries.Add(database, order, order.Status, order.Status, actorId, OrderReportMarkers.CodReceivedReason, now);

            // Gom sự kiện để phát sau commit. `Amount` là tổng tiền đơn, vì GHN thu đủ khi giao.
            events.Emit(new OrderPaidEvent(order.Id, order.Code, order.UserId, order.AnonymousId, order.Total));

            await database.SaveChangesAsync(cancellationToken);

            // Mỗi lần nhận tiền đầu tiên ghi một dòng audit trong cùng transaction.
            var entry = new AuditLogEntry(
                actorId,
                CodReceiptAuditAction,
                OrderCancellationService.AuditTargetType,
                order.Code,
                new { paymentStatus = OrderLabels.ToLabel(previousPaymentStatus) },
                new { paymentStatus = OrderLabels.ToLabel(order.PaymentStatus) });
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

    public async Task<AdminOrderDetailResponse> MarkRefundedAsync(
        string code, string actorId, string transactionRef, CancellationToken cancellationToken)
    {
        var orderId = await FindOrderIdAsync(code, cancellationToken);

        // Transaction giữ thay đổi đơn, lịch sử và audit cùng thành công hoặc cùng quay lui.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var order = await OrderRowLock.LoadForUpdateAsync(database, orderId, cancellationToken);

        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            // PUT có tính lặp lại an toàn: cùng mã giao dịch trả 200 và không ghi thêm gì.
            if (string.Equals(order.RefundTransactionRef, transactionRef, StringComparison.Ordinal))
            {
                return await orders.GetDetailAsync(code, cancellationToken);
            }

            // Mã khác nghĩa là hai nhân viên ghi hai khoản hoàn khác nhau cho một đơn. Giữ mã đã lưu và báo xung đột.
            throw new AppException(InvalidTransitionCode, 409, RefundDifferentRefMessage);
        }

        // Máy trạng thái thanh toán là nguồn luật duy nhất. Chỉ `refund_pending` đi tiếp được sang `refunded`.
        var decision = PaymentTransitions.Evaluate(order.PaymentStatus, PaymentStatus.Refunded, TransitionActor.Staff);
        if (!decision.IsAllowed)
        {
            throw new AppException(InvalidTransitionCode, 409, RefundRejectedMessage);
        }

        var previousPaymentStatus = order.PaymentStatus;
        order.PaymentStatus = PaymentStatus.Refunded;
        order.RefundTransactionRef = transactionRef;

        // Trạng thái đơn không đổi nên `fromStatus` bằng `toStatus`. Lý do ghi mã giao dịch để người xem lịch sử đối chiếu sao kê.
        var now = OrderHistoryEntries.TruncateToMicroseconds(clock.GetUtcNow().UtcDateTime);
        OrderHistoryEntries.Add(
            database, order, order.Status, order.Status, actorId, $"Đã hoàn tiền, mã giao dịch {transactionRef}.", now);

        await database.SaveChangesAsync(cancellationToken);

        // Mỗi lần hoàn tiền đầu tiên ghi một dòng audit trong cùng transaction. Audit giữ mã giao dịch.
        var entry = new AuditLogEntry(
            actorId,
            RefundAuditAction,
            OrderCancellationService.AuditTargetType,
            order.Code,
            new { paymentStatus = OrderLabels.ToLabel(previousPaymentStatus) },
            new { paymentStatus = OrderLabels.ToLabel(order.PaymentStatus), transactionRef });
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await orders.GetDetailAsync(code, cancellationToken);
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
