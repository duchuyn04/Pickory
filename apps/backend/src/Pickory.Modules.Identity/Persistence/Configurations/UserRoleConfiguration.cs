using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pickory.Modules.Identity.Entities;

namespace Pickory.Modules.Identity.Persistence.Configurations;

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> entity)
    {
        // Giữ khóa ghép để người dùng không nhận cùng vai trò hai lần.
        entity.ToTable("user_roles");
        entity.HasKey(value => new { value.UserId, value.RoleId });
        entity.Property(value => value.UserId).IsRequired();
        entity.Property(value => value.RoleId).IsRequired();
        // Xóa liên kết khi tài khoản bị xóa để không còn quyền của tài khoản cũ.
        entity.HasOne(value => value.User)
            .WithMany(user => user.Roles)
            .HasForeignKey(value => value.UserId)
            .HasConstraintName("user_roles_userId_fkey")
            .OnDelete(DeleteBehavior.Cascade);
        // Xóa liên kết khi vai trò bị xóa để quyền không tham chiếu vai trò không tồn tại.
        entity.HasOne(value => value.Role)
            .WithMany(role => role.Users)
            .HasForeignKey(value => value.RoleId)
            .HasConstraintName("user_roles_roleId_fkey")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
