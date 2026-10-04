import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { recordAudit } from './audit';
import { isPermission, PERMISSION_CATALOG, type Permission } from './permissions';

export interface CreateRoleParams {
  actorId: string;
  actorRoles: string[];
  actorPermissions: ReadonlySet<Permission>;
  code: string;
  name: string;
  permissions: string[];
}

/**
 * Lấy danh mục toàn bộ quyền trong hệ thống.
 */
export function getPermissionsCatalog() {
  return PERMISSION_CATALOG;
}

/**
 * Lấy danh sách role kèm quyền và số lượng người dùng đang giữ role.
 */
export async function getRoles() {
  const roles = await db.role.findMany({
    include: {
      permissions: true,
      users: { select: { userId: true } },
    },
    orderBy: { createdAt: 'asc' },
  });

  return roles.map((r) => ({
    id: r.id,
    code: r.code,
    name: r.name,
    isSystem: r.isSystem,
    permissions: r.permissions.map((p) => p.permission).sort(),
    userCount: r.users.length,
  }));
}

/**
 * Tạo role mới:
 * 1. Quyền phải thuộc danh mục trong code (nếu không -> 400).
 * 2. Ghi 1 dòng audit log (after).
 */
export async function createRole(params: CreateRoleParams) {
  const { actorId, actorRoles, actorPermissions, code, name, permissions } = params;
  const normalizedCode = code.trim().toLowerCase();
  const uniquePermissions = [...new Set(permissions)];

  // Kiểm tra mã hợp lệ
  if (!/^[a-z0-9_-]+$/.test(normalizedCode)) {
    throw new AppError(
      'INVALID_INPUT',
      400,
      'Mã role chỉ được chứa chữ thường, số, gạch dưới và gạch nối.',
    );
  }

  // Kiểm tra quyền thuộc danh mục
  for (const p of uniquePermissions) {
    if (!isPermission(p)) {
      throw new AppError(
        'INVALID_PERMISSION',
        400,
        `Quyền "${p}" không thuộc danh mục quyền của hệ thống.`,
      );
    }
  }

  // Chống leo thang đặc quyền: actor không phải admin chỉ được cấp quyền mà chính mình sở hữu
  if (!actorRoles.includes('admin')) {
    for (const p of uniquePermissions) {
      if (!actorPermissions.has(p as Permission)) {
        throw new AppError(
          'FORBIDDEN',
          403,
          `Bạn không thể cấp quyền "${p}" vì chính bạn không sở hữu quyền này.`,
        );
      }
    }
  }
  const existing = await db.role.findUnique({
    where: { code: normalizedCode },
  });
  if (existing) {
    throw new AppError('ROLE_ALREADY_EXISTS', 400, `Mã role "${normalizedCode}" đã tồn tại.`);
  }

  return db.$transaction(async (tx) => {
    const role = await tx.role.create({
      data: {
        code: normalizedCode,
        name: name.trim(),
        isSystem: false,
        permissions: {
          create: (uniquePermissions as Permission[]).map((p) => ({ permission: p })),
        },
      },
      include: { permissions: true },
    });

    await recordAudit(tx, {
      actorId,
      action: 'role.create',
      targetType: 'role',
      targetId: role.id,
      after: {
        code: role.code,
        name: role.name,
        permissions: role.permissions.map((p) => p.permission),
      },
    });

    return {
      id: role.id,
      code: role.code,
      name: role.name,
      isSystem: role.isSystem,
      permissions: role.permissions.map((p) => p.permission).sort(),
      userCount: 0,
    };
  });
}

export interface UpdateRoleParams {
  actorId: string;
  actorRoles: string[];
  actorPermissions: ReadonlySet<Permission>;
  roleId: string;
  name?: string;
  permissions?: string[];
}

/**
 * Sửa role:
 * 1. Role hệ thống (admin, customer) không được sửa.
 * 2. Quyền phải thuộc danh mục.
 * 3. Ghi 1 dòng audit log (before & after).
 */
export async function updateRole(params: UpdateRoleParams) {
  const { actorId, actorRoles, actorPermissions, roleId, name, permissions } = params;

  const role = await db.role.findUnique({
    where: { id: roleId },
    include: { permissions: true },
  });

  if (!role) {
    throw new AppError('ROLE_NOT_FOUND', 404, 'Không tìm thấy role.');
  }

  if (role.isSystem) {
    throw new AppError('FORBIDDEN', 403, 'Không được phép sửa đổi role hệ thống.');
  }

  // Chống leo thang đặc quyền: nhân viên không phải admin không được sửa đổi role mà chính mình đang giữ
  if (!actorRoles.includes('admin') && actorRoles.includes(role.code)) {
    throw new AppError(
      'CANNOT_MODIFY_SELF_ROLE',
      403,
      'Bạn không thể sửa đổi role mà chính mình đang giữ.',
    );
  }

  let uniquePermissions: string[] | undefined;
  if (permissions) {
    uniquePermissions = [...new Set(permissions)];
    for (const p of uniquePermissions) {
      if (!isPermission(p)) {
        throw new AppError(
          'INVALID_PERMISSION',
          400,
          `Quyền "${p}" không thuộc danh mục quyền của hệ thống.`,
        );
      }
    }

    // Chống leo thang đặc quyền: actor không phải admin chỉ được cấp quyền mà chính mình sở hữu
    if (!actorRoles.includes('admin')) {
      for (const p of uniquePermissions) {
        if (!actorPermissions.has(p as Permission)) {
          throw new AppError(
            'FORBIDDEN',
            403,
            `Bạn không thể cấp quyền "${p}" vì chính bạn không sở hữu quyền này.`,
          );
        }
      }
    }
  }
  const before = {
    name: role.name,
    permissions: role.permissions.map((p) => p.permission),
  };

  return db.$transaction(async (tx) => {
    if (uniquePermissions) {
      await tx.rolePermission.deleteMany({ where: { roleId: role.id } });
      await tx.rolePermission.createMany({
        data: (uniquePermissions as Permission[]).map((p) => ({
          roleId: role.id,
          permission: p,
        })),
      });
    }

    const updated = await tx.role.update({
      where: { id: role.id },
      data: {
        name: name !== undefined ? name.trim() : undefined,
      },
      include: {
        permissions: true,
        users: { select: { userId: true } },
      },
    });

    const after = {
      name: updated.name,
      permissions: updated.permissions.map((p) => p.permission).sort(),
    };

    await recordAudit(tx, {
      actorId,
      action: 'role.update',
      targetType: 'role',
      targetId: role.id,
      before,
      after,
    });

    return {
      id: updated.id,
      code: updated.code,
      name: updated.name,
      isSystem: updated.isSystem,
      permissions: updated.permissions.map((p) => p.permission).sort(),
      userCount: updated.users.length,
    };
  });
}

/**
 * Xóa role:
 * 1. Role hệ thống không được xóa.
 * 2. Role đang có người giữ không được xóa.
 * 3. Ghi 1 dòng audit log (before).
 */
export async function deleteRole(actorId: string, roleId: string): Promise<void> {
  const role = await db.role.findUnique({
    where: { id: roleId },
    include: {
      permissions: true,
      users: true,
    },
  });

  if (!role) {
    throw new AppError('ROLE_NOT_FOUND', 404, 'Không tìm thấy role.');
  }

  if (role.isSystem) {
    throw new AppError('FORBIDDEN', 403, 'Không được phép xóa role hệ thống.');
  }

  if (role.users.length > 0) {
    throw new AppError(
      'ROLE_IN_USE',
      400,
      `Không thể xóa role đang có ${role.users.length} người dùng giữ. Vui lòng gỡ role trước.`,
    );
  }

  await db.$transaction(async (tx) => {
    await tx.role.delete({ where: { id: role.id } });

    await recordAudit(tx, {
      actorId,
      action: 'role.delete',
      targetType: 'role',
      targetId: role.id,
      before: {
        code: role.code,
        name: role.name,
        permissions: role.permissions.map((p) => p.permission),
      },
    });
  });
}
