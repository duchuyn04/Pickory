import type { Prisma } from '../../generated/prisma/client';
import { loadConfig } from '../../shared/config';
import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { recordAudit } from './audit';
import { getEmailSender } from './email-sender';
import { createEmailToken } from './email-tokens';
import type { Permission } from './permissions';

/**
 * Lấy danh sách tài khoản nhân viên (không bao giờ lộ passwordHash).
 */
export async function getStaffList() {
  const staff = await db.user.findMany({
    where: { kind: 'staff' },
    include: {
      roles: {
        include: { role: true },
      },
    },
    orderBy: { createdAt: 'desc' },
  });

  return staff.map((u) => ({
    id: u.id,
    email: u.email,
    name: u.name,
    phone: u.phone,
    emailVerified: u.emailVerified,
    kind: u.kind,
    lockedAt: u.lockedAt,
    createdAt: u.createdAt,
    roles: u.roles.map((r) => r.role.code),
  }));
}

export interface InviteStaffParams {
  actorId: string;
  actorRoles: string[];
  actorPermissions: ReadonlySet<Permission>;
  email: string;
  name: string;
  roleCodes: string[];
}

/**
 * Khởi tạo tài khoản nhân viên mới và gửi email mời kích hoạt kèm đường dẫn thiết lập mật khẩu.
 */
export async function inviteStaff(params: InviteStaffParams) {
  const { actorId, actorRoles, actorPermissions, email, name, roleCodes } = params;
  const normalizedEmail = email.trim().toLowerCase();
  const uniqueRoleCodes = [...new Set(roleCodes)];

  // Chặn gán role customer cho tài khoản nhân viên vì nhân viên chịu cơ chế xác thực và phiên riêng biệt.
  if (uniqueRoleCodes.includes('customer')) {
    throw new AppError('INVALID_ROLE', 400, 'Không thể gán role customer cho tài khoản nhân viên.');
  }

  // Chống leo thang đặc quyền: chỉ quản trị viên (admin) mới có thẩm quyền phân quyền admin cho người khác.
  if (uniqueRoleCodes.includes('admin') && !actorRoles.includes('admin')) {
    throw new AppError(
      'FORBIDDEN',
      403,
      'Chỉ quản trị viên (admin) mới có quyền mời tài khoản làm admin.',
    );
  }
  // Kiểm tra tính duy nhất của email để không mời trùng tài khoản hoặc nhầm lẫn giữa khách và nhân viên.
  const existingUser = await db.user.findUnique({
    where: { email: normalizedEmail },
  });
  if (existingUser) {
    if (existingUser.kind === 'customer') {
      throw new AppError(
        'EMAIL_ALREADY_EXISTS',
        400,
        'Email này đã thuộc tài khoản khách hàng, không thể mời làm nhân viên.',
      );
    }
    throw new AppError(
      'EMAIL_ALREADY_EXISTS',
      400,
      'Email này đã được sử dụng cho một nhân viên khác.',
    );
  }
  // Kiểm tra sự tồn tại của tất cả các role được chỉ định để không gán các mã role sai lệch.
  const roles = await db.role.findMany({
    where: { code: { in: uniqueRoleCodes } },
    include: { permissions: true },
  });

  if (roles.length !== uniqueRoleCodes.length) {
    throw new AppError('INVALID_ROLE', 400, 'Một hoặc nhiều mã role không tồn tại trong hệ thống.');
  }

  // Chống leo thang đặc quyền: actor không phải admin chỉ được mời role có quyền nằm trong actorPermissions
  if (!actorRoles.includes('admin')) {
    const grantedPerms = new Set(roles.flatMap((r) => r.permissions.map((p) => p.permission)));
    for (const p of grantedPerms) {
      if (!actorPermissions.has(p as Permission)) {
        throw new AppError(
          'FORBIDDEN',
          403,
          `Bạn không thể mời nhân viên với role chứa quyền "${p}" vì chính bạn không sở hữu quyền này.`,
        );
      }
    }
  }
  return db.$transaction(async (tx) => {
    const newUser = await tx.user.create({
      data: {
        email: normalizedEmail,
        name: name.trim(),
        emailVerified: false,
        kind: 'staff',
        roles: {
          create: roles.map((r) => ({ roleId: r.id })),
        },
      },
      include: {
        roles: { include: { role: true } },
      },
    });

    // Cấp token kích hoạt có thời hạn 72 giờ để nhân viên mới có đủ thời gian tiếp nhận thư mời và đặt mật khẩu.
    const token = await createEmailToken(
      {
        userId: newUser.id,
        purpose: 'invite',
        expiresInMs: 72 * 60 * 60 * 1000,
      },
      tx,
    );

    // Ghi nhật ký kiểm toán để theo dõi người gửi lời mời và danh sách vai trò dự kiến bàn giao.
    await recordAudit(tx, {
      actorId,
      action: 'staff.invite',
      targetType: 'user',
      targetId: newUser.id,
      after: {
        email: newUser.email,
        name: newUser.name,
        roles: uniqueRoleCodes,
      },
    });

    const config = loadConfig();
    await getEmailSender().send({
      to: newUser.email,
      subject: 'Lời mời tham gia quản trị Pickory',
      text: `Chào bạn, bạn đã được mời tham gia đội ngũ quản trị Pickory. Vui lòng thiết lập mật khẩu tại liên kết sau (hạn 72 giờ): ${config.WEB_ORIGIN}/reset-password?token=${token}`,
      metadata: { token, purpose: 'invite' },
    });

    return {
      id: newUser.id,
      email: newUser.email,
      name: newUser.name,
      roles: newUser.roles.map((r) => r.role.code),
    };
  });
}

/**
 * Đếm số lượng quản trị viên (admin) đang hoạt động (chưa bị khóa và chưa bị xóa).
 */
async function countActiveAdmins(tx: Prisma.TransactionClient): Promise<number> {
  const adminRole = await tx.role.findUnique({ where: { code: 'admin' } });
  if (!adminRole) return 0;

  return tx.user.count({
    where: {
      kind: 'staff',
      lockedAt: null,
      deletedAt: null,
      roles: {
        some: { roleId: adminRole.id },
      },
    },
  });
}

/**
 * Kiểm tra quyền quản lý nhân viên mục tiêu:
 * Nếu actor không phải admin, mọi quyền trong mọi role của nhân viên mục tiêu phải thuộc actorPermissions.
 */
function assertActorCanManageTargetRoles(
  actorRoles: string[],
  actorPermissions: ReadonlySet<Permission>,
  targetUserRoles: Array<{ role: { permissions: Array<{ permission: string }> } }>,
): void {
  // Quản trị viên (admin) có toàn quyền quản lý, bỏ qua kiểm tra tập con quyền
  if (actorRoles.includes('admin')) {
    return;
  }

  // Duyệt lồng trực tiếp từng quyền của từng role để không tạo Set hợp quyền trung gian
  for (const userRole of targetUserRoles) {
    for (const rolePerm of userRole.role.permissions) {
      if (!actorPermissions.has(rolePerm.permission as Permission)) {
        throw new AppError(
          'FORBIDDEN',
          403,
          `Bạn không có quyền quản lý nhân viên sở hữu quyền "${rolePerm.permission}".`,
        );
      }
    }
  }
}

export interface AssignStaffRolesParams {
  actorId: string;
  actorRoles: string[];
  actorPermissions: ReadonlySet<Permission>;
  targetUserId: string;
  roleCodes: string[];
}
/**
 * Điều chỉnh danh sách vai trò được phân công cho một nhân viên nội bộ.
 */
export async function assignStaffRoles(params: AssignStaffRolesParams) {
  const { actorId, actorRoles, actorPermissions, targetUserId, roleCodes } = params;
  const uniqueRoleCodes = [...new Set(roleCodes)];

  // Không cho phép tự sửa role của chính mình nhằm ngăn nhân viên tự ý nâng đặc quyền hoặc gỡ bỏ ràng buộc.
  if (actorId === targetUserId) {
    throw new AppError('CANNOT_MODIFY_SELF', 400, 'Không thể tự sửa đổi role của chính mình.');
  }

  // Phân tách hoàn toàn giữa nhân viên và khách hàng, không thể gán role customer trong luồng quản trị nhân sự.
  if (uniqueRoleCodes.includes('customer')) {
    throw new AppError('INVALID_ROLE', 400, 'Không thể gán role customer cho nhân viên.');
  }

  // Chống leo thang đặc quyền: chỉ admin mới có quyền gán role admin
  if (uniqueRoleCodes.includes('admin') && !actorRoles.includes('admin')) {
    throw new AppError('FORBIDDEN', 403, 'Chỉ quản trị viên (admin) mới có quyền gán role admin.');
  }

  const newRoles = await db.role.findMany({
    where: { code: { in: uniqueRoleCodes } },
    include: { permissions: true },
  });

  if (newRoles.length !== uniqueRoleCodes.length) {
    throw new AppError('INVALID_ROLE', 400, 'Một hoặc nhiều mã role không hợp lệ.');
  }

  // Chống leo thang đặc quyền: actor không phải admin chỉ được gán role có quyền nằm trong actorPermissions
  if (!actorRoles.includes('admin')) {
    const grantedPerms = new Set(newRoles.flatMap((r) => r.permissions.map((p) => p.permission)));
    for (const p of grantedPerms) {
      if (!actorPermissions.has(p as Permission)) {
        throw new AppError(
          'FORBIDDEN',
          403,
          `Bạn không thể gán role chứa quyền "${p}" vì chính bạn không sở hữu quyền này.`,
        );
      }
    }
  }

  try {
    return await db.$transaction(
      async (tx) => {
        const targetUser = await tx.user.findUnique({
          where: { id: targetUserId },
          include: {
            roles: {
              include: {
                role: {
                  include: {
                    permissions: true,
                  },
                },
              },
            },
          },
        });

        if (!targetUser || targetUser.kind !== 'staff') {
          throw new AppError('USER_NOT_FOUND', 404, 'Không tìm thấy tài khoản nhân viên.');
        }

        const currentRoleCodes = targetUser.roles.map((r) => r.role.code);
        const isCurrentlyAdmin = currentRoleCodes.includes('admin');
        const willBeAdmin = uniqueRoleCodes.includes('admin');

        // Chống hạ quyền admin: chỉ admin mới được gỡ role của một admin khác
        if (isCurrentlyAdmin && !actorRoles.includes('admin')) {
          throw new AppError(
            'FORBIDDEN',
            403,
            'Chỉ quản trị viên (admin) mới có quyền sửa đổi quyền của một admin khác.',
          );
        }

        // Lý do: Nhân viên không phải admin chỉ được sửa đổi role nếu sở hữu toàn bộ quyền hiện tại của mục tiêu.
        assertActorCanManageTargetRoles(actorRoles, actorPermissions, targetUser.roles);

        // Bảo vệ hệ thống khỏi trạng thái mất quyền quản trị hoàn toàn bằng cách không cho phép gỡ admin cuối cùng đang hoạt động.
        if (isCurrentlyAdmin && !willBeAdmin && targetUser.lockedAt === null) {
          const activeAdminCount = await countActiveAdmins(tx);
          if (activeAdminCount <= 1) {
            throw new AppError(
              'LAST_ADMIN_PROTECTED',
              400,
              'Không thể gỡ quyền admin của quản trị viên cuối cùng còn hoạt động.',
            );
          }
        }

        await tx.userRole.deleteMany({ where: { userId: targetUserId } });
        if (newRoles.length > 0) {
          await tx.userRole.createMany({
            data: newRoles.map((r) => ({
              userId: targetUserId,
              roleId: r.id,
            })),
          });
        }

        // Thu hồi ngay lập tức mọi phiên đăng nhập nếu nhân viên bị tước toàn bộ role để họ không tiếp tục thao tác trái phép.
        if (newRoles.length === 0) {
          await tx.session.deleteMany({ where: { userId: targetUserId } });
        }

        // Ghi lại sự thay đổi vai trò vào nhật ký kiểm toán để phục vụ thanh tra phân quyền nội bộ.
        await recordAudit(tx, {
          actorId,
          action: 'staff.roles_update',
          targetType: 'user',
          targetId: targetUserId,
          before: { roles: currentRoleCodes },
          after: { roles: uniqueRoleCodes },
        });

        return {
          userId: targetUserId,
          roles: uniqueRoleCodes,
        };
      },
      { isolationLevel: 'Serializable' },
    );
  } catch (err: unknown) {
    if (typeof err === 'object' && err !== null && 'code' in err && err.code === 'P2034') {
      throw new AppError('CONFLICT', 409, 'Xung đột giao dịch đồng thời, vui lòng thử lại.');
    }
    throw err;
  }
}
export interface StaffLockParams {
  actorId: string;
  actorRoles: string[];
  actorPermissions: ReadonlySet<Permission>;
  targetUserId: string;
}

/**
 * Khóa tài khoản nhân viên nội bộ và chấm dứt toàn bộ phiên làm việc của tài khoản đó.
 */
export async function lockStaff(params: StaffLockParams): Promise<void> {
  const { actorId, actorRoles, actorPermissions, targetUserId } = params;

  // Ngăn chặn nhân viên tự khóa chính mình để tránh vô tình mất quyền truy cập hoặc làm gián đoạn vận hành hệ thống.
  if (actorId === targetUserId) {
    throw new AppError('CANNOT_MODIFY_SELF', 400, 'Không thể tự khóa tài khoản của chính mình.');
  }

  try {
    return await db.$transaction(
      async (tx) => {
        const targetUser = await tx.user.findUnique({
          where: { id: targetUserId },
          include: {
            roles: {
              include: {
                role: {
                  include: {
                    permissions: true,
                  },
                },
              },
            },
          },
        });

        if (!targetUser || targetUser.kind !== 'staff') {
          throw new AppError('USER_NOT_FOUND', 404, 'Không tìm thấy tài khoản nhân viên.');
        }

        const isCurrentlyAdmin = targetUser.roles.some((r) => r.role.code === 'admin');

        // Chống khóa admin: chỉ admin mới được khóa một admin khác
        if (isCurrentlyAdmin && !actorRoles.includes('admin')) {
          throw new AppError(
            'FORBIDDEN',
            403,
            'Chỉ quản trị viên (admin) mới có quyền khóa tài khoản của một admin.',
          );
        }

        // Lý do: Kiểm tra quyền trước khi thực hiện no-op nếu tài khoản đã bị khóa.
        assertActorCanManageTargetRoles(actorRoles, actorPermissions, targetUser.roles);

        // Nếu tài khoản đã bị khóa từ trước thì bỏ qua bước cập nhật nhưng vẫn giữ tính idempotent.
        if (targetUser.lockedAt !== null) {
          return;
        }

        // Đếm số lượng admin đang hoạt động để ngăn chặn khóa tài khoản admin cuối cùng, tránh sự cố mất kiểm soát hệ thống.
        if (isCurrentlyAdmin) {
          const activeAdminCount = await countActiveAdmins(tx);
          if (activeAdminCount <= 1) {
            throw new AppError(
              'LAST_ADMIN_PROTECTED',
              400,
              'Không thể khóa quản trị viên cuối cùng còn hoạt động.',
            );
          }
        }

        const now = new Date();
        await tx.user.update({
          where: { id: targetUserId },
          data: { lockedAt: now },
        });

        // Thu hồi ngay lập tức toàn bộ phiên đăng nhập để ngăn chặn nhân viên bị khóa tiếp tục thao tác trên hệ thống.
        await tx.session.deleteMany({ where: { userId: targetUserId } });

        // Ghi nhận hành động khóa vào nhật ký kiểm toán để xác minh trách nhiệm của người thực hiện.
        await recordAudit(tx, {
          actorId,
          action: 'staff.lock',
          targetType: 'user',
          targetId: targetUserId,
          before: { lockedAt: null },
          after: { lockedAt: now.toISOString() },
        });
      },
      { isolationLevel: 'Serializable' },
    );
  } catch (err: unknown) {
    if (typeof err === 'object' && err !== null && 'code' in err && err.code === 'P2034') {
      throw new AppError('CONFLICT', 409, 'Xung đột giao dịch đồng thời, vui lòng thử lại.');
    }
    throw err;
  }
}

/**
 * Mở khóa tài khoản nhân viên để khôi phục quyền truy cập vào các tính năng quản trị.
 */
export async function unlockStaff(params: StaffLockParams): Promise<void> {
  const { actorId, actorRoles, actorPermissions, targetUserId } = params;

  try {
    return await db.$transaction(
      async (tx) => {
        const targetUser = await tx.user.findUnique({
          where: { id: targetUserId },
          include: {
            roles: {
              include: {
                role: {
                  include: {
                    permissions: true,
                  },
                },
              },
            },
          },
        });

        if (!targetUser || targetUser.kind !== 'staff') {
          throw new AppError('USER_NOT_FOUND', 404, 'Không tìm thấy tài khoản nhân viên.');
        }

        // Chỉ admin mới có thẩm quyền mở khóa cho một tài khoản admin khác nhằm duy trì kỷ luật phân quyền cấp cao.
        const isCurrentlyAdmin = targetUser.roles.some((r) => r.role.code === 'admin');
        if (isCurrentlyAdmin && !actorRoles.includes('admin')) {
          throw new AppError(
            'FORBIDDEN',
            403,
            'Chỉ quản trị viên (admin) mới có quyền mở khóa tài khoản của một admin.',
          );
        }

        // Lý do: Nhân viên không phải admin chỉ được mở khóa mục tiêu nếu sở hữu toàn bộ quyền hiện tại của mục tiêu.
        assertActorCanManageTargetRoles(actorRoles, actorPermissions, targetUser.roles);

        await tx.user.update({
          where: { id: targetUserId },
          data: { lockedAt: null },
        });

        // Ghi nhật ký kiểm toán để lưu vết thời điểm và người mở khóa tài khoản nhân viên.
        await recordAudit(tx, {
          actorId,
          action: 'staff.unlock',
          targetType: 'user',
          targetId: targetUserId,
          before: { lockedAt: targetUser.lockedAt },
          after: { lockedAt: null },
        });
      },
      { isolationLevel: 'Serializable' },
    );
  } catch (err: unknown) {
    if (typeof err === 'object' && err !== null && 'code' in err && err.code === 'P2034') {
      throw new AppError('CONFLICT', 409, 'Xung đột giao dịch đồng thời, vui lòng thử lại.');
    }
    throw err;
  }
}
