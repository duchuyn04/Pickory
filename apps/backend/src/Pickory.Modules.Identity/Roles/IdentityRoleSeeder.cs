using Microsoft.EntityFrameworkCore;
using Pickory.Modules.Identity.Entities;
using Pickory.Modules.Identity.Persistence;

namespace Pickory.Modules.Identity.Roles;

// Tạo role mặc định và quyền mặc định. Chạy lại nhiều lần vẫn an toàn.
public static class IdentityRoleSeeder
{
    public static async Task SeedAsync(IdentityDbContext database, CancellationToken cancellationToken)
    {
        foreach (var defaultRole in DefaultRoles.All)
        {
            // Tìm role theo mã. Mã role là khóa duy nhất, nên seed không tạo role trùng.
            var role = await database.Roles
                .Include(value => value.Permissions)
                .SingleOrDefaultAsync(value => value.Code == defaultRole.Code, cancellationToken);
            if (role is null)
            {
                role = new Role { Code = defaultRole.Code };
                database.Roles.Add(role);
            }

            // Cập nhật tên và cờ hệ thống. Role cũ trong DB luôn khớp định nghĩa trong code.
            role.Name = defaultRole.Name;
            role.IsSystem = defaultRole.IsSystem;

            // Chỉ thêm quyền mặc định còn thiếu. Seed không xóa quyền mà admin đã gán thêm.
            foreach (var permission in defaultRole.Permissions)
            {
                if (role.Permissions.All(existing => existing.Permission != permission))
                {
                    role.Permissions.Add(new RolePermission { Permission = permission });
                }
            }

            // Lưu từng role giống bản Node. Mỗi role có `createdAt` riêng, nên danh sách role giữ thứ tự tạo ổn định.
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
