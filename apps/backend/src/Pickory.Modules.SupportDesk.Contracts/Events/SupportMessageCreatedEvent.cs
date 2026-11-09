using Pickory.Shared.Events;

namespace Pickory.Modules.SupportDesk.Contracts.Events;

// Phát sau khi một tin của nhân viên hoặc tin hệ thống đã commit. Kênh SSE của khách dùng sự kiện này.
// Handler bỏ qua sự kiện có vai `customer`, nên tin của khách không phát ngược qua kênh khách.
public sealed record SupportMessageCreatedEvent(
    string ConversationId,
    string MessageId,
    string Role,
    string Body,
    string? Action,
    DateTime CreatedAt,
    string? StaffDisplayName = null,
    string? SessionId = null) : IDomainEvent
{
    // Vai nhân viên trong dữ liệu SSE. Giá trị khớp nhãn Postgres của `SupportMessageRole`.
    public const string StaffRole = "staff";

    // Vai hệ thống trong dữ liệu SSE. Tin hệ thống có thể mang `action` của form.
    public const string SystemRole = "system";
}
