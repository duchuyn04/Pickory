import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { recordAudit } from './audit';
import { isPermission, PERMISSION_CATALOG, type Permission } from './permissions';

export interface CreateRoleParams {
  actorId: string;
  // Role và quyền của người đang thao tác, dùng để chặn họ cấp quyền vượt quá quyền của chính họ.
  actorRoles: string[];
  actorPermissions: ReadonlySet<Permission>;
  code: string;
  name: string;
  permissions: string[];
}

// Danh mục quyền (từ code) để giao diện vẽ bảng tick.
export function getPermissionsCatalog() {
  return PERMISSION_CATALOG;
}

// Danh sách role kèm quyền và số người đang giữ.
export async function getRoles() {
  const roles = await db.role.findMany({
    include: {
      permissions: true,
      users: { select: { userId: true } },
    },
    // Theo thứ tự tạo để role tạo sẵn luôn ở trên, role admin thêm sau nằm dưới.
    orderBy: { createdAt: 'asc' },
  });

  return roles.map((r) => ({
    id: r.id,
    code: r.code,
    name: r.name,
    isSystem: r.isSystem,
    // Sắp xếp để thứ tự quyền ổn định giữa các lần gọi, giao diện không bị nhảy.
    permissions: r.permissions.map((p) => p.permission).sort(),
    // Giao diện cần số này để biết role nào xóa được (chưa ai giữ).
    userCount: r.users.length,
  }));
}

// Tạo role mới với bộ quyền được chọn.
export async function createRole(params: CreateRoleParams) {
  const { actorId, actorRoles, actorPermissions, code, name, permissions } = params;
  // Chuẩn hóa mã về chữ thường để 'Kho' và 'kho' không thành hai role.
  const normalizedCode = code.trim().toLowerCase();
  // Khử trùng vì cùng một quyền gửi hai lần sẽ vi phạm khóa chính (roleId, permission) và gây lỗi 500.
  const uniquePermissions = [...new Set(permissions)];

  // Mã role chỉ gồm ký tự an toàn để dùng được trong URL và không chứa ký tự gây lỗi.
  if (!/^[a-z0-9_-]+$/.test(normalizedCode)) {
    throw new AppError(
      'INVALID_INPUT',
      400,
      'Mã role chỉ được chứa chữ thường, số, gạch dưới và gạch nối.',
    );
  }

  // Client gửi gì lên cũng không tin: chỉ chấp nhận quyền có trong danh mục của code.
  for (const p of uniquePermissions) {
    if (!isPermission(p)) {
      throw new AppError(
        'INVALID_PERMISSION',
        400,
        `Quyền "${p}" không thuộc danh mục quyền của hệ thống.`,
      );
    }
  }

  // Chống leo thang đặc quyền: người không phải admin chỉ cấp được quyền họ đang có,
  // nếu không họ tạo role đủ quyền rồi gán cho một tài khoản do họ kiểm soát để nâng quyền cho mình.
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

  // Kiểm tra trùng mã trước để trả lỗi 400 dễ hiểu thay vì lỗi ràng buộc duy nhất của DB.
  const existing = await db.role.findUnique({
    where: { code: normalizedCode },
  });
  if (existing) {
    throw new AppError('ROLE_ALREADY_EXISTS', 400, `Mã role "${normalizedCode}" đã tồn tại.`);
  }

  // Tạo role và ghi nhật ký trong cùng transaction để không có role nào được tạo mà thiếu dấu vết.
  return db.$transaction(async (tx) => {
    const role = await tx.role.create({
      data: {
        code: normalizedCode,
        name: name.trim(),
        // Role tạo qua giao diện luôn là role thường, chỉ role tạo sẵn mới là role hệ thống.
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
      // Chỉ có after vì trước đó role chưa tồn tại.
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
      // Role vừa tạo chưa có ai giữ.
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

// Sửa tên hoặc bộ quyền của một role.
export async function updateRole(params: UpdateRoleParams) {
  const { actorId, actorRoles, actorPermissions, roleId, name, permissions } = params;

  const role = await db.role.findUnique({
    where: { id: roleId },
    include: { permissions: true },
  });

  if (!role) {
    throw new AppError('ROLE_NOT_FOUND', 404, 'Không tìm thấy role.');
  }

  // Role hệ thống (admin, customer) giữ nguyên để quy tắc phân quyền cốt lõi không bị phá.
  if (role.isSystem) {
    throw new AppError('FORBIDDEN', 403, 'Không được phép sửa đổi role hệ thống.');
  }

  // Người không phải admin không được sửa role mình đang giữ, nếu không họ tự thêm quyền cho chính mình.
  if (!actorRoles.includes('admin') && actorRoles.includes(role.code)) {
    throw new AppError(
      'CANNOT_MODIFY_SELF_ROLE',
      403,
      'Bạn không thể sửa đổi role mà chính mình đang giữ.',
    );
  }

  let uniquePermissions: string[] | undefined;
  // Chỉ kiểm tra quyền khi request có gửi danh sách quyền; không gửi nghĩa là chỉ đổi tên.
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

    // Giống createRole: người không phải admin chỉ gán được quyền mình đang có.
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

  // Lưu trạng thái trước khi sửa để nhật ký có đủ before và after.
  const before = {
    name: role.name,
    permissions: role.permissions.map((p) => p.permission),
  };

  return db.$transaction(async (tx) => {
    if (uniquePermissions) {
      // Xóa hết rồi tạo lại thay vì so sánh từng quyền, đơn giản hơn và cho kết quả giống nhau.
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
        // undefined nghĩa là không đổi tên.
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

    // Quyền của role đổi ở đây có hiệu lực ngay cho mọi người giữ role, vì quyền được nạp lại mỗi request.
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

// Xóa một role chưa ai giữ.
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

  // Role hệ thống không xóa được, vì nhiều logic dựa vào chúng (admin có mọi quyền, customer cho khách).
  if (role.isSystem) {
    throw new AppError('FORBIDDEN', 403, 'Không được phép xóa role hệ thống.');
  }

  // Phải gỡ role khỏi mọi người trước khi xóa, nếu không xóa xong họ mất quyền mà không ai biết lý do.
  if (role.users.length > 0) {
    throw new AppError(
      'ROLE_IN_USE',
      400,
      `Không thể xóa role đang có ${role.users.length} người dùng giữ. Vui lòng gỡ role trước.`,
    );
  }

  await db.$transaction(async (tx) => {
    // Xóa role; các dòng role_permissions tự bị xóa theo (onDelete: Cascade trong schema).
    await tx.role.delete({ where: { id: role.id } });

    // Chỉ có before vì sau khi xóa không còn trạng thái nào.
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
