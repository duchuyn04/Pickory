import type { PrismaClient } from '../../generated/prisma/client';
import { DEFAULT_ROLE_PERMISSIONS, INITIAL_ROLES } from './permissions';

/**
 * Seed 4 role mặc định và quyền tương ứng theo spec/SPEC-identity.md.
 * Hoạt động idempotent (chạy lại an toàn không sinh trùng hay lỗi).
 */
export async function seedIdentityRoles(prisma: PrismaClient): Promise<void> {
  for (const roleDef of INITIAL_ROLES) {
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
