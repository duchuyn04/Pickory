using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.Identity.Entities;

namespace Pickory.Modules.Identity.Persistence.Configurations;

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> entity)
    {
        // Giữ khóa ghép để một quyền không xuất hiện hai lần trong cùng vai trò.
        entity.ToTable("role_permissions");
        entity.HasKey(value => new { value.RoleId, value.Permission });
        entity.Property(value => value.RoleId).IsRequired();
        entity.Property(value => value.Permission).IsRequired();
        // Xóa quyền khi vai trò bị xóa để không còn quyền tham chiếu vai trò không tồn tại.
        entity.HasOne(value => value.Role)
            .WithMany(role => role.Permissions)
            .HasForeignKey(value => value.RoleId)
            .HasConstraintName("role_permissions_roleId_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
