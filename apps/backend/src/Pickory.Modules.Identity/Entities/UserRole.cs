namespace Pickory.Modules.Identity.Entities;

// Liên kết nhiều nhiều giữa tài khoản người dùng và vai trò bảo mật.
public sealed class UserRole
{
    public string UserId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;

    public User? User { get; set; }
    public Role? Role { get; set; }
}
