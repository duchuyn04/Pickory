namespace Pickory.Modules.SupportDesk.Entities;

// `Shared` đánh dấu mẫu dùng chung của đội. `OwnerId` rỗng nghĩa là mẫu không thuộc nhân viên nào.
internal sealed class CannedResponse
{
    public string Id { get; set; } = null!;
    public string? OwnerId { get; set; }
    public string Shortcut { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool Shared { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
