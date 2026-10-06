using System.Text.Json;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;

namespace Pickory.Modules.Identity.Audit;

// Thêm một dòng nhật ký thao tác của nhân viên vào `DbContext` của thao tác chính.
// Người gọi lưu dòng này trong cùng giao dịch, nên thao tác bị hủy thì nhật ký cũng không còn.
public static class AuditRecorder
{
    public static AuditLog Add(
        IdentityDbContext database,
        string actorId,
        string action,
        string targetType,
        string targetId,
        object? before,
        object? after)
    {
        var auditLog = new AuditLog
        {
            // Lưu id người thực hiện. Lịch sử vẫn đúng khi tên người đó đổi.
            ActorId = actorId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            // Luôn làm sạch ở đây thay vì tin người gọi. Nhật ký không bao giờ lưu bí mật.
            Before = ToJsonDocument(before),
            After = ToJsonDocument(after),
        };
        database.AuditLogs.Add(auditLog);
        return auditLog;
    }

    // Trạng thái bị thiếu thành JSON `null`, giống `Prisma.JsonNull` ở bản Node. Cột không chứa SQL `NULL`.
    private static JsonDocument ToJsonDocument(object? value)
    {
        var sanitized = AuditDataSanitizer.Sanitize(value);
        return JsonDocument.Parse(sanitized?.ToJsonString() ?? "null");
    }
}
