namespace Pickory.Modules.Identity.Entities;

// Tài khoản liên kết từ nhà cung cấp danh tính bên ngoài.
public sealed class OAuthAccount
{
    public string Id { get; set; } = null!;
    public string Provider { get; set; } = string.Empty;
    public string ProviderUserId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}
