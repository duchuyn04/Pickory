namespace Pickory.Modules.SupportDesk.Entities;

// Một tin trong hội thoại. `StaffId` chỉ có khi `Role` là nhân viên.
internal sealed class SupportMessage
{
    public string Id { get; set; } = null!;
    public string ConversationId { get; set; } = string.Empty;
    public SupportMessageRole Role { get; set; }
    public string? StaffId { get; set; }

    // Mã phiên hỗ trợ chứa tin nhắn này. Giá trị để rỗng khi tin không thuộc phiên nào.
    public string? SessionId { get; set; }
    public string Body { get; set; } = string.Empty;

    // Cho khung chat biết form cần hiện kèm tin hệ thống, ví dụ `callback_form` hoặc `csat_form`.
    public string? Action { get; set; }

    public DateTime CreatedAt { get; set; }
}
