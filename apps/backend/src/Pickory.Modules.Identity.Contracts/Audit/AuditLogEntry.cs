namespace Pickory.Modules.Identity.Contracts.Audit;

// Một thao tác của nhân viên cần ghi nhật ký, với trạng thái trước và sau thao tác.
// Identity giấu trường nhạy cảm trong `Before` và `After` trước khi lưu.
public sealed record AuditLogEntry(
    string ActorId,
    string Action,
    string TargetType,
    string TargetId,
    object? Before,
    object? After);
