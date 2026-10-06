namespace Pickory.Modules.Identity.Entities;

// Phiên đăng nhập hoạt động được định danh bằng chuỗi băm của token.
public sealed class Session
{
    public string Id { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}
