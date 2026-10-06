namespace Pickory.Modules.Identity.Entities;

// Hồ sơ ghi nhận sự đồng thuận chính sách từ người dùng hoặc khách vãng lai.
public sealed class ConsentRecord
{
    public string Id { get; set; } = null!;
    public string AnonymousId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public bool Personalization { get; set; }
    public bool Marketing { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}
