using System.Text.Json;

namespace Pickory.Modules.Identity.Features.AdminAuditLogs;

// Giao diện hiển thị email và tên người thực hiện thay vì chuỗi UUID (Universally Unique Identifier).
// `Before` và `After` giữ nguyên JSON đã lưu, vì cấu trúc phụ thuộc loại thao tác.
public sealed record AuditLogItemResponse(
    string Id,
    string ActorId,
    string? ActorEmail,
    string? ActorName,
    string Action,
    string TargetType,
    string TargetId,
    JsonElement? Before,
    JsonElement? After,
    DateTime CreatedAt);
