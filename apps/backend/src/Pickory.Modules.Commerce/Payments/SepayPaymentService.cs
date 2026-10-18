using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pickory.Modules.Commerce.Contracts.Events;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Entities;
using Pickory.Modules.Commerce.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Shared.Events;

namespace Pickory.Modules.Commerce.Payments;

internal sealed class SepayPaymentService(
    CommerceDbContext database, IEventDispatcher dispatcher, TimeProvider clock,
    SepayOrderMatcher orderMatcher, ILogger<SepayPaymentService> logger)
{
    public async Task ReceiveAsync(SepayWebhookPayload payload, CancellationToken cancellationToken)
    {
        // Chuyển ID thành chuỗi thập phân ổn định để mọi lần gửi lại có cùng khóa chống trùng.
        var providerId = payload.Id.ToString(CultureInfo.InvariantCulture);
        // Tra ID trước để giao dịch gửi lại không mở thêm transaction khớp.
        if (await database.PaymentTransactions.AnyAsync(row => row.ProviderTxId == providerId, cancellationToken))
        {
            return;
        }
        var payment = new PaymentTransaction
        {
            Provider = "sepay", ProviderTxId = providerId, Amount = payload.Amount,
            Content = payload.Content, Raw = payload.Raw, MatchStatus = PaymentMatchStatuses.Unmatched, OrderId = null,
            ReceivedAt = clock.GetUtcNow().UtcDateTime,
        };
        try
        {
            // Transaction đầu chỉ lưu chứng từ thô, nên lỗi khớp sau đó không làm mất giao dịch.
            await using var rawTransaction = await database.Database.BeginTransactionAsync(cancellationToken);
            database.PaymentTransactions.Add(payment);
            // Ghi chứng từ chưa gắn đơn để nhân viên vẫn thấy tiền nếu bước khớp thất bại.
            await database.SaveChangesAsync(cancellationToken);
            // Commit riêng chứng từ trước khi khóa đơn, bảo vệ dữ liệu đối soát khỏi rollback khớp.
            await rawTransaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "payment_transactions_providerTxId_key" })
        {
            // Request khác đã thắng ràng buộc unique, nên request này không được khớp lại giao dịch.
            database.ChangeTracker.Clear();
            return;
        }
        var events = new DomainEventCollector(dispatcher);
        try
        {
            // Transaction thứ hai giữ riêng thay đổi đơn và kết quả khớp khỏi chứng từ đã commit.
            await using var matchingTransaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await MatchAsync(payment, payload, events, cancellationToken);
            // Lưu kết quả khớp cùng trạng thái và lịch sử đơn trong transaction thứ hai.
            await database.SaveChangesAsync(cancellationToken);
            // Commit thành công là điều kiện bắt buộc trước khi phát sự kiện đã trả tiền.
            await matchingTransaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Rollback khớp giữ dòng thô `unmatched` cho đối soát tay, không phát sự kiện của thay đổi đã hủy.
            events.Discard();
            database.ChangeTracker.Clear();
            // Log không nhận header hoặc payload, nên khóa không đi vào thông điệp lỗi của dịch vụ.
            logger.LogError(exception, "Khớp giao dịch SePay {ProviderTxId} thất bại, cần đối soát tay.", providerId);
            return;
        }
        // Handler chỉ thấy dữ liệu đã commit, kể cả khi đọc qua kết nối DB khác.
        await events.FlushAsync(CancellationToken.None);
    }

    private async Task MatchAsync(PaymentTransaction payment, SepayWebhookPayload payload,
        DomainEventCollector events, CancellationToken cancellationToken)
    {
        // Đọc cấu hình thật tại thời điểm khớp, không dùng số tài khoản từ biến môi trường.
        var accountNumber = await database.Settings.AsNoTracking().Select(row => row.SepayAccountNumber)
            .FirstOrDefaultAsync(cancellationToken);
        // Tiền ra hoặc tài khoản chưa cấu hình không được ghi nhận thanh toán của khách.
        if (payload.TransferType != "in" || string.IsNullOrEmpty(accountNumber) || payload.AccountNumber != accountNumber)
        {
            payment.MatchStatus = PaymentMatchStatuses.Ignored;
            return;
        }
        // Parser trả mã đầu tiên đã chuẩn hóa, không dùng trường code tùy chọn của SePay.
        var orderCode = TransferContentParser.FindOrderCode(payload.Content);
        if (orderCode is null)
        {
            return;
        }
        // Khóa dòng trước khi đọc trạng thái để hai giao dịch cùng đơn không cùng phát sự kiện đã trả tiền.
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "id" FROM orders WHERE "code" = {orderCode} FOR UPDATE""", cancellationToken);
        // Đọc sau khi giữ khóa để thấy trạng thái mới nhất của request đã commit trước đó.
        var order = await database.Orders.SingleOrDefaultAsync(row => row.Code == orderCode, cancellationToken);
        if (order is null)
        {
            return;
        }
        // Luật khớp dùng chung với route gắn tay, nên webhook không giữ bản sao của luật.
        var matched = await orderMatcher.TryMatchAsync(order, payment, events, actorId: null, cancellationToken);
        // Đơn COD, đã trả hoặc hủy vì lý do khác giữ chứng từ chưa khớp cho đối soát.
        if (!matched)
        {
            logger.LogInformation("Giao dịch SePay không khớp đơn {OrderCode} ở trạng thái {Status}.", order.Code, order.Status);
        }
    }
}
