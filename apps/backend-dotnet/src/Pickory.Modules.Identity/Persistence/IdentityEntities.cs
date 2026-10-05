using System.Text.Json;

namespace Pickory.Modules.Identity.Persistence;

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

// Vai trò bảo mật dùng để nhóm quyền truy cập cho người dùng.
public sealed class Role
{
    public string Id { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
    public ICollection<UserRole> Users { get; set; } = new List<UserRole>();
}

// Quyền hạn chi tiết được gán trực tiếp cho từng vai trò.
public sealed class RolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;

    public Role? Role { get; set; }
}

// Liên kết nhiều nhiều giữa tài khoản người dùng và vai trò bảo mật.
public sealed class UserRole
{
    public string UserId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;

    public User? User { get; set; }
    public Role? Role { get; set; }
}

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

// Bản ghi nhật ký kiểm toán lưu lại lịch sử biến động dữ liệu.
public sealed class AuditLog
{
    public string Id { get; set; } = null!;
    public string ActorId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public JsonDocument? Before { get; set; }
    public JsonDocument? After { get; set; }
    public DateTime CreatedAt { get; set; }
}
