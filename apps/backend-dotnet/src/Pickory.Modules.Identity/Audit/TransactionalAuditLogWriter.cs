using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Contracts.Audit;
using Pickory.Modules.Identity.Persistence;

namespace Pickory.Modules.Identity.Audit;

// Ghi nhật ký thao tác cho module khác, trên kết nối và transaction của module đó.
public sealed class TransactionalAuditLogWriter : IAuditLogWriter
{
    public async Task WriteAsync(AuditLogEntry entry, DbTransaction transaction, CancellationToken cancellationToken)
    {
        // Transaction đã đóng thì không còn kết nối. Ghi trên kết nối mới sẽ tách nhật ký khỏi thao tác chính.
        var connection = transaction.Connection
            ?? throw new InvalidOperationException("Transaction không còn kết nối tới DB.");

        // Dùng chung kết nối và transaction. Cấu hình mới và nhật ký cùng commit hoặc cùng bị hủy.
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.ConfigureOptions(options, connection);
        await using var identity = new IdentityDbContext(options.Options);
        await identity.Database.UseTransactionAsync(transaction, cancellationToken);

        // `AuditRecorder` giấu trường nhạy cảm, giống nhật ký của các thao tác trong identity.
        AuditRecorder.Add(identity, entry.ActorId, entry.Action, entry.TargetType, entry.TargetId, entry.Before, entry.After);
        await identity.SaveChangesAsync(cancellationToken);
    }
}
