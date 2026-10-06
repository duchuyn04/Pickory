using System.Text.Json;

namespace Pickory.Modules.Identity.Entities;

// Bản ghi nhật ký kiểm toán lưu lại lịch sử biến động dữ liệu.
public sealed class AuditLog
{
    public string Id { get; set; } = null!;
    public string ActorId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public JsonDocument? Before { get; set; }
    public JsonDocument? After { get; set; }
    public DateTime CreatedAt { get; set; }
}
