using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pickory.Modules.Commerce.Contracts.Orders;
using Pickory.Modules.Commerce.Persistence;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Commerce.Payments.Admin;

// File `.xlsx` đã dựng xong trong bộ nhớ, cùng tên file đã ghi vào nhật ký thao tác.
internal sealed record PaymentTransactionExportFile(byte[] Content, string FileName);

// Xuất giao dịch SePay ra Excel. Dùng chung bộ lọc với danh sách, nhưng đọc mọi giao dịch khớp thay vì một trang.
internal sealed class AdminPaymentTransactionExportService(
    CommerceDbContext database,
    ExportOptions exportOptions,
    IAuditLogWriter auditLogWriter,
    TimeProvider clock)
{
    public const string ExportAuditAction = "commerce.payment-transactions.export";
    public const string ExportAuditTargetType = "export";
    public const string FileNamePrefix = "giao-dich-sepay";

    public async Task<PaymentTransactionExportFile> ExportAsync(
        AdminPaymentTransactionListQuery filter,
        string actorId,
        CancellationToken cancellationToken)
    {
        // Đọc `MaxRows + 1` giao dịch. Dòng thứ `MaxRows + 1` chứng minh danh sách vượt giới hạn mà không cần câu `COUNT`.
        var sources = await ReadTransactionsAsync(filter, exportOptions.MaxRows + 1, cancellationToken);

        // Kiểm giới hạn trước khi dựng file. Danh sách quá lớn không tạo file và không ghi nhật ký.
        ExportLimits.EnsureWithinLimit(sources.Count, exportOptions.MaxRows);

        // Chụp thời điểm một lần. Tên file và nhật ký dùng cùng một tên.
        var fileName = ExportFileName.Create(FileNamePrefix, clock.GetUtcNow());

        // Dựng file trước khi mở transaction. Dựng file lỗi thì không có nhật ký nào được ghi.
        var content = XlsxWorkbookWriter.Write(
        [
            PaymentTransactionExportSheets.BuildSheet(sources.Select(PaymentTransactionExportSheets.BuildRow)),
        ]);

        // Nhật ký và commit nằm sau khi file đã dựng xong. Ghi nhật ký lỗi thì ném lỗi và route không trả file.
        await WriteAuditLogAsync(filter, actorId, fileName, sources.Count, cancellationToken);
        return new PaymentTransactionExportFile(content, fileName);
    }

    private async Task<List<PaymentTransactionExportSource>> ReadTransactionsAsync(
        AdminPaymentTransactionListQuery filter,
        int takeCount,
        CancellationToken cancellationToken)
    {
        var filtered = AdminPaymentTransactionFilterQuery.Apply(database.PaymentTransactions.AsNoTracking(), filter);

        // Thứ tự giống danh sách: mới nhận tiền lên đầu, rồi theo `Id`.
        return await AdminPaymentTransactionFilterQuery.NewestFirst(filtered)
            .Take(takeCount)
            // Chỉ chiếu cột cần ghi ra file, thẳng vào `PaymentTransactionExportSource`. Cột `raw` không bao giờ rời DB.
            .Select(row => new PaymentTransactionExportSource(
                row.ProviderTxId,
                row.ReceivedAt,
                row.Amount,
                row.Content,
                row.MatchStatus,
                row.Order != null ? row.Order.Code : null,
                row.Order != null ? (int?)row.Order.Total : null,
                row.Order != null ? (PaymentStatus?)row.Order.PaymentStatus : null,
                row.Order != null ? row.Order.Flags : null))
            .ToListAsync(cancellationToken);
    }

    private async Task WriteAuditLogAsync(
        AdminPaymentTransactionListQuery filter,
        string actorId,
        string fileName,
        int rowCount,
        CancellationToken cancellationToken)
    {
        // Transaction giữ nhật ký và commit cùng một đơn vị. `IAuditLogWriter` bắt buộc nhận transaction của module gọi.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Bảng `audit_logs` chỉ có `before` và `after`. `after` mang bộ lọc và số giao dịch, tương ứng `details` của contract.
        var entry = new AuditLogEntry(
            actorId,
            ExportAuditAction,
            ExportAuditTargetType,
            fileName,
            null,
            new { filters = PaymentTransactionExportAuditFilters.From(filter), rowCount });
        await auditLogWriter.WriteAsync(entry, transaction.GetDbTransaction(), cancellationToken);

        // Commit xong mới trả file. Commit lỗi thì ném lỗi và nhân viên không nhận file không có dấu vết.
        await transaction.CommitAsync(cancellationToken);
    }
}
