namespace Pickory.Modules.SupportDesk.Entities;

// Yêu cầu gọi lại của khách khi ngoài giờ hoặc khi chờ quá lâu. Số điện thoại bị xóa sau 30 ngày, nên `Phone` có thể rỗng.
internal sealed class CallbackRequest
{
    public string Id { get; set; } = null!;
    public string? ConversationId { get; set; }

    // `AnonymousId` phục vụ giới hạn 3 lần mỗi ngày, `CustomerUserId` phục vụ `UserDeletedEvent`.
    public string AnonymousId { get; set; } = string.Empty;
    public string? CustomerUserId { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public CallbackRequestStatus Status { get; set; }
    public string? HandledBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? HandledAt { get; set; }
}
