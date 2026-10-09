namespace Pickory.Modules.Identity.Entities;

// Tài khoản người dùng trong hệ thống với thông tin đăng nhập và hồ sơ cơ bản.
public sealed class User
{
    public string Id { get; set; } = null!;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public string? PasswordHash { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public UserKind Kind { get; set; }
    public DateTime? LockedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<UserRole> Roles { get; set; } = new List<UserRole>();
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
    public ICollection<OAuthAccount> OAuthAccounts { get; set; } = new List<OAuthAccount>();
    public ICollection<EmailToken> EmailTokens { get; set; } = new List<EmailToken>();
    public ICollection<ConsentRecord> ConsentRecords { get; set; } = new List<ConsentRecord>();
}
