namespace Pickory.Modules.Catalog.Entities;

// Tác vụ dọn dẹp tập tin hình ảnh trên dịch vụ lưu trữ ngoài.
public sealed class ImageDeletionJob
{
    public string Id { get; set; } = null!;
    public string PublicId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 8;
    public DateTime NextRetryAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
