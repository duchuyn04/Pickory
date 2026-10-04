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
 * Tạo vai trò người dùng mới trong hệ thống kèm danh sách quyền được cấp.
 */
export async function createRole(params: CreateRoleParams) {
  const { actorId, actorRoles, actorPermissions, code, name, permissions } = params;
  const normalizedCode = code.trim().toLowerCase();
  const uniquePermissions = [...new Set(permissions)];

  // Giới hạn định dạng mã vai trò theo slug chuẩn để an toàn khi dùng trong URL và logic kiểm tra quyền.
  if (!/^[a-z0-9_-]+$/.test(normalizedCode)) {
    throw new AppError(
      'INVALID_INPUT',
      400,
      'Mã role chỉ được chứa chữ thường, số, gạch dưới và gạch nối.',
    );
  }

  // Kiểm tra từng quyền để chắc chắn không cấp các quyền lạ nằm ngoài danh mục hệ thống quy định.
  for (const p of uniquePermissions) {
    if (!isPermission(p)) {
      throw new AppError(
        'INVALID_PERMISSION',
        400,
        `Quyền "${p}" không thuộc danh mục quyền của hệ thống.`,
      );
    }
  }

  // Chống leo thang đặc quyền: nhân viên không thể tạo role chứa quyền cao hơn các quyền chính mình đang có.
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
  // Mã vai trò là định danh duy nhất để tránh xung đột cấu hình phân quyền trong toàn hệ thống.
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

    // Ghi nhật ký kiểm toán để theo dõi người tạo và bộ quyền ban đầu của vai trò mới.
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
 * Cập nhật tên hoặc danh sách quyền hạn được gán cho một vai trò.
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

  // Bảo vệ các vai trò cốt lõi như admin hoặc customer khỏi bị thay đổi cấu hình ngoài dự kiến.
  if (role.isSystem) {
    throw new AppError('FORBIDDEN', 403, 'Không được phép sửa đổi role hệ thống.');
  }

  // Chống leo thang đặc quyền: nhân viên không thể tự bổ sung quyền cho vai trò mà chính mình đang nắm giữ.
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
    // Đảm bảo toàn bộ quyền được cập nhật đều thuộc danh mục quyền hợp lệ được hỗ trợ.
    for (const p of uniquePermissions) {
      if (!isPermission(p)) {
        throw new AppError(
          'INVALID_PERMISSION',
          400,
          `Quyền "${p}" không thuộc danh mục quyền của hệ thống.`,
        );
      }
    }

    // Chống leo thang đặc quyền: người sửa không được gán quyền mà bản thân họ không sở hữu.
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
    // Thay thế toàn bộ liên kết quyền cũ bằng bộ quyền mới trong cùng transaction để bảo đảm tính nhất quán.
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
 * Xóa một vai trò tùy chỉnh khỏi hệ thống khi không còn người dùng nào nắm giữ.
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

  // Chặn xóa vai trò hệ thống để không phá vỡ logic kiểm tra quyền cốt lõi.
  if (role.isSystem) {
    throw new AppError('FORBIDDEN', 403, 'Không được phép xóa role hệ thống.');
  }

  // Bắt buộc gỡ vai trò khỏi tất cả người dùng trước khi xóa để tránh để lại trạng thái phân quyền mồ côi.
  if (role.users.length > 0) {
    throw new AppError(
      'ROLE_IN_USE',
      400,
      `Không thể xóa role đang có ${role.users.length} người dùng giữ. Vui lòng gỡ role trước.`,
    );
  }

  await db.$transaction(async (tx) => {
    await tx.role.delete({ where: { id: role.id } });

    // Ghi lại trạng thái trước khi xóa vào nhật ký kiểm toán để phục vụ tra cứu lịch sử khi cần thiết.
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
