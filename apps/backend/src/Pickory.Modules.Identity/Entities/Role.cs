namespace Pickory.Modules.Identity.Entities;

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
