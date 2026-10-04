import type { PrismaClient } from '../../generated/prisma/client';
import { DEFAULT_ROLE_PERMISSIONS, INITIAL_ROLES } from './permissions';

/**
 * Khởi tạo các vai trò và quyền mặc định ban đầu vào cơ sở dữ liệu với tính chất idempotent.
 */
export async function seedIdentityRoles(prisma: PrismaClient): Promise<void> {
  for (const roleDef of INITIAL_ROLES) {
    // Dùng upsert để đảm bảo quá trình chạy seed nhiều lần không gây lỗi trùng lặp dữ liệu.
    const role = await prisma.role.upsert({
      where: { code: roleDef.code },
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

    const defaultPerms = DEFAULT_ROLE_PERMISSIONS[roleDef.code] ?? [];
    for (const permission of defaultPerms) {
      // Dùng upsert theo cặp khóa (roleId, permission) để giữ tính toàn vẹn khi gán quyền mặc định.
      await prisma.rolePermission.upsert({
        where: {
          roleId_permission: {
            roleId: role.id,
            permission,
          },
        },
        update: {},
        create: {
          roleId: role.id,
          permission,
        },
      });
    }
  }
}
