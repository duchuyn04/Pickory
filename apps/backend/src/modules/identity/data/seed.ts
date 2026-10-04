import type { PrismaClient } from '../../../generated/prisma/client';
import { DEFAULT_ROLE_PERMISSIONS, INITIAL_ROLES } from './permissions';

// Tạo 4 role mặc định và quyền của chúng; chạy lại nhiều lần vẫn an toàn.
export async function seedIdentityRoles(prisma: PrismaClient): Promise<void> {
  for (const roleDef of INITIAL_ROLES) {
    // upsert thay vì create để chạy seed lần hai không lỗi trùng mã role.
    const role = await prisma.role.upsert({
      where: { code: roleDef.code },
      // Cập nhật tên và cờ hệ thống để role cũ luôn khớp với định nghĩa trong code.
      update: {
        name: roleDef.name,
        isSystem: roleDef.isSystem,
      },
      create: {
        code: roleDef.code,
        name: roleDef.name,
        isSystem: roleDef.isSystem,
      },
    });

    // Role không có trong bảng quyền mặc định (ví dụ admin) thì không có dòng nào; admin có mọi quyền ngầm định trong code.
    const defaultPerms = DEFAULT_ROLE_PERMISSIONS[roleDef.code] ?? [];
    for (const permission of defaultPerms) {
      // Khóa chính là cặp (roleId, permission) nên upsert theo cặp này, không tạo trùng.
      await prisma.rolePermission.upsert({
        where: {
          roleId_permission: {
            roleId: role.id,
            permission,
          },
        },
        // update rỗng vì dòng đã tồn tại thì không có gì cần đổi; chỉ bổ sung quyền mặc định còn thiếu.
        update: {},
        create: {
          roleId: role.id,
          permission,
        },
      });
    }
  }
}
