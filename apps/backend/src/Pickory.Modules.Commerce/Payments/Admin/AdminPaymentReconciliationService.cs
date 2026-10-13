using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Events;
using Pickory.Shared.Exceptions;

namespace Pickory.Modules.Commerce.Payments.Admin;

// Đối soát tay: nhân viên gắn giao dịch SePay chưa khớp vào đơn, hoặc đánh dấu không thuộc đơn nào.
// Mỗi thao tác chạy trong một transaction, khóa dòng giao dịch rồi dòng đơn, ghi audit cùng commit.
internal sealed class AdminPaymentReconciliationService(
    CommerceDbContext database,
    SepayOrderMatcher orderMatcher,
    IAuditLogWriter auditLogWriter,
    IEventDispatcher dispatcher)
{
    public const string AttachAuditAction = "commerce.payment.attach";
    public const string IgnoreAuditAction = "commerce.payment.ignore";
    public const string AuditTargetType = "payment_transaction";

    private const string UnmatchedStatus = "unmatched";
    private const string IgnoredStatus = "ignored";
    private const string InvalidMatchCode = "INVALID_PAYMENT_MATCH";
    private const string TransactionNotFoundMessage = "Không tìm thấy giao dịch.";
    private const string OrderNotFoundMessage = "Không tìm thấy đơn hàng.";
    private const string AlreadyResolvedMessage = "Giao dịch đã được khớp hoặc đã đánh dấu bỏ qua.";
    private const string OrderCannotReceiveMessage = "Đơn không ở trạng thái nhận được tiền chuyển khoản.";

    public async Task<AdminPaymentTransactionResponse> AttachAsync(
        string transactionId, string orderCode, string actorId, CancellationToken cancellationToken)
    {
        // Mã đơn không tồn tại báo 404 trước khi mở transaction, nên không giữ khóa hay kết nối.
        var orderId = await database.Orders.AsNoTracking()
            .Where(order => order.Code == orderCode)
            .Select(order => order.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (orderId is null)
        {
            throw new AppException("ORDER_NOT_FOUND", 404, OrderNotFoundMessage);
        }

        // Transaction giữ thay đổi đơn, giao dịch, lịch sử và audit cùng thành công hoặc cùng quay lui.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Khóa dòng giao dịch trước khi đọc `MatchStatus`, để hai request cùng giao dịch xếp hàng và chỉ một request đạt.
        var payment = await LoadUnmatchedForUpdateAsync(transactionId, cancellationToken);

        // Khóa dòng đơn giống webhook, để việc ghi nhận thanh toán không chạy song song với webhook hay worker hết hạn.
        var order = await OrderRowLock.LoadForUpdateAsync(database, orderId, cancellationToken);

        var events = new DomainEventCollector(dispatcher);
        try
        {
            // Luật khớp dùng chung với webhook. Mã nhân viên đi vào lịch sử đơn để phân biệt với khớp tự động.
            var matched = await orderMatcher.TryMatchAsync(order, payment, events, actorId, cancellationToken);

            // Webhook để chứng từ chưa khớp khi đơn không nhận được tiền. Route tay báo lỗi để nhân viên chọn đơn khác.
            if (!matched)
            {
                throw new AppException(InvalidMatchCode, 409, OrderCannotReceiveMessage);
            }

            await database.SaveChangesAsync(cancellationToken);

            // Mỗi lần gắn thành công ghi một dòng audit trong cùng transaction. Audit giữ mã đơn đã gắn.
            var entry = new AuditLogEntry(
                actorId,
                AttachAuditAction,
                AuditTargetType,
                payment.Id,
                new { matchStatus = UnmatchedStatus, orderCode = (string?)null },
                new { matchStatus = payment.MatchStatus, orderCode = order.Code });
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
        return ToResponse(payment, order);
    }

    public async Task<AdminPaymentTransactionResponse> IgnoreAsync(
        string transactionId, string actorId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Khóa dòng giao dịch trước khi đọc `MatchStatus`, để một giao dịch không vừa bị gắn vừa bị bỏ qua.
        var payment = await LoadUnmatchedForUpdateAsync(transactionId, cancellationToken);

        // Đánh dấu bỏ qua không có đơn gắn kèm, nên `OrderId` giữ `null`.
        payment.MatchStatus = IgnoredStatus;
        await database.SaveChangesAsync(cancellationToken);

        // Mỗi lần đánh dấu ghi một dòng audit trong cùng transaction.
        var entry = new AuditLogEntry(
            actorId,
            IgnoreAuditAction,
            AuditTargetType,
            payment.Id,
            new { matchStatus = UnmatchedStatus, orderCode = (string?)null },
            new { matchStatus = IgnoredStatus, orderCode = (string?)null });
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return ToResponse(payment, order: null);
    }

    // Phải gọi sau khi đã mở transaction. Khóa giữ đến khi transaction kết thúc.
    private async Task<PaymentTransaction> LoadUnmatchedForUpdateAsync(string transactionId, CancellationToken cancellationToken)
    {
        // Khóa dòng bằng SELECT FOR UPDATE để các thao tác cùng giao dịch xếp hàng, không chạy song song.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "id" FROM payment_transactions WHERE "id" = {transactionId} FOR UPDATE""", cancellationToken);

        // Đọc sau khi giữ khóa để thấy trạng thái mà thao tác trước vừa commit.
        var payment = await database.PaymentTransactions
            .SingleOrDefaultAsync(row => row.Id == transactionId, cancellationToken);
        if (payment is null)
        {
            throw new AppException("PAYMENT_TRANSACTION_NOT_FOUND", 404, TransactionNotFoundMessage);
        }

        // Giao dịch đã khớp hoặc đã bỏ qua là kết quả đã chốt, nên không cho đổi lần hai.
        if (payment.MatchStatus != UnmatchedStatus)
        {
            throw new AppException(InvalidMatchCode, 409, AlreadyResolvedMessage);
        }

        return payment;
    }

    private static AdminPaymentTransactionResponse ToResponse(PaymentTransaction payment, Order? order)
    {
        return new AdminPaymentTransactionResponse(
            payment.Id,
            payment.ProviderTxId,
            payment.Amount,
            payment.Content,
            payment.MatchStatus,
            payment.ReceivedAt,
            order is null
                ? null
                : new AdminPaymentOrderSummaryResponse(
                    order.Code,
                    OrderLabels.ToLabel(order.Status),
                    OrderLabels.ToLabel(order.PaymentStatus),
                    order.Total,
                    order.Flags));
    }
}
