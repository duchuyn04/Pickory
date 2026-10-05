namespace Pickory.Modules.Identity.Entities;

// Quyền hạn chi tiết được gán trực tiếp cho từng vai trò.
public sealed class RolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;

    public Role? Role { get; set; }
}
