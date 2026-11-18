using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Audit;
using Pickory.Modules.Identity.Persistence;
using Pickory.Shared.Exports;

namespace Pickory.Modules.Identity.Features.AdminCustomers;

// File `.xlsx` đã dựng xong trong bộ nhớ, cùng tên file đã ghi vào nhật ký thao tác.
internal sealed record CustomerExportFile(byte[] Content, string FileName);

// Xuất khách ra Excel. Dùng chung bộ lọc với danh sách, nhưng đọc mọi khách khớp thay vì một trang.
internal sealed class CustomerExportService(IdentityDbContext database, ExportOptions exportOptions, TimeProvider clock)
{
    public const string ExportAuditAction = "identity.customers.export";
    public const string ExportAuditTargetType = "export";
    public const string FileNamePrefix = "khach-hang";

    public async Task<CustomerExportFile> ExportAsync(
        CustomerListQuery filter,
        string actorId,
        CancellationToken cancellationToken)
    {
        // Đọc `MaxRows + 1` khách. Dòng thứ `MaxRows + 1` chứng minh danh sách vượt giới hạn mà không cần câu `COUNT`.
        var sources = await ReadCustomersAsync(filter, exportOptions.MaxRows + 1, cancellationToken);

        // Kiểm giới hạn trước khi dựng file. Danh sách quá lớn không tạo file và không ghi nhật ký.
        ExportLimits.EnsureWithinLimit(sources.Count, exportOptions.MaxRows);

        // Chụp thời điểm một lần. Tên file và nhật ký dùng cùng một tên.
        var fileName = ExportFileName.Create(FileNamePrefix, clock.GetUtcNow());

        // Dựng file trước khi ghi nhật ký. Dựng file lỗi thì không có nhật ký nào được ghi.
        var content = XlsxWorkbookWriter.Write(
        [
            CustomerExportSheets.BuildSheet(sources.Select(CustomerExportSheets.BuildRow)),
        ]);

        // Nhật ký nằm sau khi file đã dựng xong. Ghi nhật ký lỗi thì ném lỗi và route không trả file.
        await WriteAuditLogAsync(filter, actorId, fileName, sources.Count, cancellationToken);
        return new CustomerExportFile(content, fileName);
    }

    private async Task<List<CustomerExportSource>> ReadCustomersAsync(
        CustomerListQuery filter,
        int takeCount,
        CancellationToken cancellationToken)
    {
        var filtered = CustomerListFilter.Apply(database.Users.AsNoTracking(), filter);

        // Thứ tự giống danh sách: khách mới đăng ký lên đầu, rồi theo `Id`.
        return await CustomerListFilter.NewestFirst(filtered)
            .Take(takeCount)
            // Chỉ chiếu cột cần ghi ra file. `PasswordHash` và token không bao giờ rời DB.
            .Select(user => new CustomerExportSource(
                user.Name,
                user.Email,
                user.Phone,
                user.EmailVerified,
                user.LockedAt,
                user.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task WriteAuditLogAsync(
        CustomerListQuery filter,
        string actorId,
        string fileName,
        int rowCount,
        CancellationToken cancellationToken)
    {
        // Bảng `audit_logs` chỉ có `before` và `after`. `after` mang bộ lọc và số khách, tương ứng `details` của contract.
        AuditRecorder.Add(
            database,
            actorId,
            ExportAuditAction,
            ExportAuditTargetType,
            fileName,
            before: null,
            after: new { filters = CustomerExportAuditFilters.From(filter), rowCount });

        // Lưu một dòng nhật ký. Lưu lỗi thì ném lỗi và nhân viên không nhận file không có dấu vết.
        await database.SaveChangesAsync(cancellationToken);
    }
}
