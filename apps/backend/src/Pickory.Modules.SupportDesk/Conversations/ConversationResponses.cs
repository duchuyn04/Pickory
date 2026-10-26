using Pickory.Modules.SupportDesk.Entities;

namespace Pickory.Modules.SupportDesk.Conversations;

// Response của `POST /api/support/conversations`. `ShowRetentionNotice` chỉ bằng `true` khi hội thoại vừa được tạo.
internal sealed record ConversationResponse(string Id, string Status, DateTime CreatedAt, bool ShowRetentionNotice);

// Một tin trong lịch sử. Response không có `staffId`, vì tên nhân viên chưa được chốt.
internal sealed record MessageResponse(string Id, string Role, string Body, string? Action, DateTime CreatedAt)
{
    public static MessageResponse From(SupportMessage message) => new(
        message.Id,
        // Tên enum viết thường khớp nhãn Postgres `customer`, `staff`, `system`.
        message.Role.ToString().ToLowerInvariant(),
        message.Body,
        message.Action,
        message.CreatedAt);
}

internal sealed record MessagePageResponse(IReadOnlyList<MessageResponse> Items, string? NextCursor);
