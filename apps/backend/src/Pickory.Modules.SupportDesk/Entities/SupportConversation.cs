namespace Pickory.Modules.SupportDesk.Entities;

// Hội thoại của một khách với CSKH. Khách đăng nhập có `CustomerUserId`, khách vãng lai chỉ có `AnonymousId`.
internal sealed class SupportConversation
{
    public string Id { get; set; } = null!;
    public string? CustomerUserId { get; set; }
    public string AnonymousId { get; set; } = string.Empty;
    public SupportConversationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
