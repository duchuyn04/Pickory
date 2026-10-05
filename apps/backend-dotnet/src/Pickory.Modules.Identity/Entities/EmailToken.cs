namespace Pickory.Modules.Identity.Entities;

// Mã token tạm thời gửi qua email cho các thao tác xác thực.
public sealed class EmailToken
{
    public string Id { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public EmailTokenPurpose Purpose { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}
