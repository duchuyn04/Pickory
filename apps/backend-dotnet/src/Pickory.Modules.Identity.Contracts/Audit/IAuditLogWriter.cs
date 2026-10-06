using System.Data.Common;

namespace Pickory.Modules.Identity.Contracts.Audit;

// Module khác ghi nhật ký thao tác vào bảng của identity mà không cần biết `DbContext` của identity.
public interface IAuditLogWriter
{
    // Ghi trong transaction của module gọi. Thao tác chính bị hủy thì nhật ký cũng bị hủy.
    Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken);
}
