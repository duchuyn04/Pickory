import type { Prisma } from '../../generated/prisma/client';
import { loadConfig } from '../../shared/config';
import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { recordAudit } from './audit';
import { getEmailSender } from './email-sender';
import { createEmailToken } from './email-tokens';
import type { Permission } from './permissions';

// Danh sách nhân viên cho trang quản lý.
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

  // Chọn từng trường thay vì trả cả đối tượng user, để passwordHash không bao giờ ra khỏi API.
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
  // Role và quyền của người mời, dùng để chặn họ mời người có quyền lớn hơn mình.
  actorRoles: string[];
  actorPermissions: ReadonlySet<Permission>;
  email: string;
  name: string;
  roleCodes: string[];
}

// Mời nhân viên mới: tạo tài khoản chưa có mật khẩu và gửi link đặt mật khẩu hạn 72 giờ.
export async function inviteStaff(params: InviteStaffParams) {
  const { actorId, actorRoles, actorPermissions, email, name, roleCodes } = params;
  const normalizedEmail = email.trim().toLowerCase();
  // Khử trùng để role gửi hai lần không làm sai phép so sánh số lượng role ở bên dưới.
  const uniqueRoleCodes = [...new Set(roleCodes)];

  // Tài khoản khách và nhân viên tách riêng, nên nhân viên không được mang role customer.
  if (uniqueRoleCodes.includes('customer')) {
    throw new AppError('INVALID_ROLE', 400, 'Không thể gán role customer cho tài khoản nhân viên.');
  }

  // Chống leo thang đặc quyền: chỉ admin mới tạo thêm được admin, vì admin có mọi quyền.
  if (uniqueRoleCodes.includes('admin') && !actorRoles.includes('admin')) {
    throw new AppError(
      'FORBIDDEN',
      403,
      'Chỉ quản trị viên (admin) mới có quyền mời tài khoản làm admin.',
    );
  }

  // Email là duy nhất toàn hệ thống, nên email đã dùng (khách hoặc nhân viên) không mời được.
  const existingUser = await db.user.findUnique({
    where: { email: normalizedEmail },
  });

  if (existingUser) {
    // Hai thông báo khác nhau vì người gọi ở đây là admin đã đăng nhập, không phải người lạ cần che giấu.
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

  // Lấy kèm quyền của từng role để kiểm tra bước leo thang đặc quyền bên dưới.
  const roles = await db.role.findMany({
    where: { code: { in: uniqueRoleCodes } },
    include: { permissions: true },
  });

  // Số role tìm được ít hơn số yêu cầu nghĩa là có mã role không tồn tại.
  if (roles.length !== uniqueRoleCodes.length) {
    throw new AppError('INVALID_ROLE', 400, 'Một hoặc nhiều mã role không tồn tại trong hệ thống.');
  }

  // Chống leo thang đặc quyền: người không phải admin chỉ được mời vào role có quyền họ đang có.
  // Nếu không, họ mời một email do chính họ kiểm soát vào role mạnh hơn rồi dùng tài khoản đó.
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

  // Tạo tài khoản, token và nhật ký trong một transaction để không có tài khoản nào được mời mà thiếu token hay dấu vết.
  return db.$transaction(async (tx) => {
    const newUser = await tx.user.create({
      data: {
        email: normalizedEmail,
        name: name.trim(),
        // Chưa xác thực và chưa có mật khẩu; nhân viên tự đặt mật khẩu qua link mời.
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

    // 72 giờ theo spec, dài hơn link quên mật khẩu vì người được mời có thể chưa mở thư ngay.
    const token = await createEmailToken(
      {
        userId: newUser.id,
        purpose: 'invite',
        expiresInMs: 72 * 60 * 60 * 1000,
      },
      tx,
    );

    // Chỉ có after vì tài khoản chưa tồn tại trước thao tác này.
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
      // Link đầy đủ gồm WEB_ORIGIN vì đường dẫn tương đối không mở được từ hòm thư.
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

// Đếm admin đang hoạt động, dùng để không cho khóa hay gỡ quyền admin cuối cùng.
// Nhận tx để đếm trong cùng transaction với thao tác ghi, tránh hai người cùng đếm ra 2 rồi cùng khóa.
async function countActiveAdmins(tx: Prisma.TransactionClient): Promise<number> {
  const adminRole = await tx.role.findUnique({ where: { code: 'admin' } });
  // Không có role admin thì không có admin nào để đếm.
  if (!adminRole) return 0;

  return tx.user.count({
    where: {
      kind: 'staff',
      // Admin bị khóa hoặc đã xóa không còn là "admin hoạt động".
      lockedAt: null,
      deletedAt: null,
      roles: {
        some: { roleId: adminRole.id },
      },
    },
  });
}

// Người không phải admin chỉ được quản lý nhân viên có quyền nằm gọn trong quyền của mình; admin thì không bị giới hạn.
function assertActorCanManageTargetRoles(
  actorRoles: string[],
  actorPermissions: ReadonlySet<Permission>,
  targetUserRoles: Array<{ role: { permissions: Array<{ permission: string }> } }>,
): void {
  // Admin có mọi quyền nên mọi mục tiêu đều nằm trong quyền của họ.
  if (actorRoles.includes('admin')) {
    return;
  }

  // Duyệt trực tiếp từng quyền của từng role, không cần gộp thành một Set trung gian.
  for (const userRole of targetUserRoles) {
    for (const rolePerm of userRole.role.permissions) {
      // Chỉ cần một quyền của mục tiêu mà actor không có là từ chối: nếu không, người có quyền thấp khóa hay hạ được người cao hơn.
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

// Đặt lại toàn bộ role của một nhân viên.
export async function assignStaffRoles(params: AssignStaffRolesParams) {
  const { actorId, actorRoles, actorPermissions, targetUserId, roleCodes } = params;
  const uniqueRoleCodes = [...new Set(roleCodes)];

  // Không ai tự sửa role của chính mình, kể cả admin, để không tự nâng quyền hay tự gỡ ràng buộc.
  if (actorId === targetUserId) {
    throw new AppError('CANNOT_MODIFY_SELF', 400, 'Không thể tự sửa đổi role của chính mình.');
  }

  // Tài khoản khách và nhân viên tách riêng, nên không gán customer cho nhân viên.
  if (uniqueRoleCodes.includes('customer')) {
    throw new AppError('INVALID_ROLE', 400, 'Không thể gán role customer cho nhân viên.');
  }

  // Chống leo thang đặc quyền: chỉ admin mới gán được role admin.
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

  // Chống leo thang đặc quyền: người không phải admin chỉ gán được role có quyền họ đang có.
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

  // Bọc try để đổi lỗi xung đột transaction (P2034) thành 409 dễ hiểu ở cuối hàm.
  try {
    // Serializable: hai thao tác đồng thời trên cùng dữ liệu không thể cùng vượt kiểm tra "admin cuối cùng".
    return await db.$transaction(
      async (tx) => {
        // Đọc mục tiêu trong transaction để dữ liệu dùng kiểm tra và dữ liệu ghi là cùng một thời điểm.
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

        // Chỉ nhân viên mới có role để gán; id của khách cũng báo không tìm thấy.
        if (!targetUser || targetUser.kind !== 'staff') {
          throw new AppError('USER_NOT_FOUND', 404, 'Không tìm thấy tài khoản nhân viên.');
        }

        const currentRoleCodes = targetUser.roles.map((r) => r.role.code);
        const isCurrentlyAdmin = currentRoleCodes.includes('admin');
        const willBeAdmin = uniqueRoleCodes.includes('admin');

        // Chỉ admin mới đụng được vào role của một admin khác, nếu không người thường hạ quyền được admin.
        if (isCurrentlyAdmin && !actorRoles.includes('admin')) {
          throw new AppError(
            'FORBIDDEN',
            403,
            'Chỉ quản trị viên (admin) mới có quyền sửa đổi quyền của một admin khác.',
          );
        }

        // Người không phải admin chỉ sửa role của người có quyền nằm gọn trong quyền của mình.
        assertActorCanManageTargetRoles(actorRoles, actorPermissions, targetUser.roles);

        // Không cho gỡ role admin của admin cuối cùng, nếu không hệ thống không còn ai quản trị.
        // Bỏ qua khi admin đó đang bị khóa, vì họ vốn đã không được tính là admin hoạt động.
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

        // Xóa hết rồi gán lại thay vì so sánh từng role, đơn giản hơn và cho kết quả giống nhau.
        await tx.userRole.deleteMany({ where: { userId: targetUserId } });
        if (newRoles.length > 0) {
          await tx.userRole.createMany({
            data: newRoles.map((r) => ({
              userId: targetUserId,
              roleId: r.id,
            })),
          });
        }

        // Bị gỡ hết role thì không còn lý do giữ phiên, xóa luôn để mất hiệu lực ngay.
        // Trường hợp đổi sang role khác thì không cần xóa, vì quyền được nạp lại mỗi request nên có hiệu lực ngay.
        if (newRoles.length === 0) {
          await tx.session.deleteMany({ where: { userId: targetUserId } });
        }

        // Ghi cả before và after để xem lại được role đổi từ gì sang gì.
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
    // P2034 là Prisma báo hai transaction Serializable xung đột; trả 409 để client biết có thể thử lại.
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

// Khóa nhân viên: không đăng nhập được và phiên đang mở mất hiệu lực ngay.
export async function lockStaff(params: StaffLockParams): Promise<void> {
  const { actorId, actorRoles, actorPermissions, targetUserId } = params;

  // Không tự khóa mình, kẻo vô tình mất quyền truy cập; việc này nên do người khác làm.
  if (actorId === targetUserId) {
    throw new AppError('CANNOT_MODIFY_SELF', 400, 'Không thể tự khóa tài khoản của chính mình.');
  }

  try {
    // Serializable vì kiểm tra "admin cuối cùng" và việc khóa phải xảy ra như một khối không bị xen vào.
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

        // Chỉ admin mới khóa được admin khác, nếu không người thường khóa được cả quản trị viên.
        if (isCurrentlyAdmin && !actorRoles.includes('admin')) {
          throw new AppError(
            'FORBIDDEN',
            403,
            'Chỉ quản trị viên (admin) mới có quyền khóa tài khoản của một admin.',
          );
        }

        // Kiểm tra quyền trước nhánh "đã khóa rồi", để người không đủ quyền cũng không thăm dò được trạng thái khóa.
        assertActorCanManageTargetRoles(actorRoles, actorPermissions, targetUser.roles);

        // Đã khóa sẵn thì thoát ra, không ghi thêm nhật ký hay đổi lockedAt; gọi lại nhiều lần cho kết quả như nhau.
        if (targetUser.lockedAt !== null) {
          return;
        }

        // Khóa admin cuối cùng làm hệ thống mất người quản trị, nên chặn.
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

        // Dùng chung một mốc thời gian cho DB và nhật ký để hai nơi khớp nhau.
        const now = new Date();
        await tx.user.update({
          where: { id: targetUserId },
          data: { lockedAt: now },
        });

        // Xóa phiên để người bị khóa mất hiệu lực ngay, không phải chờ phiên 12 giờ hết hạn.
        await tx.session.deleteMany({ where: { userId: targetUserId } });

        // Lưu mốc thời gian dạng chuỗi ISO vì cột before/after là JSON.
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

// Mở khóa nhân viên để họ đăng nhập lại được.
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

        // Chỉ admin mới mở khóa được admin khác, cùng lý do với khóa.
        const isCurrentlyAdmin = targetUser.roles.some((r) => r.role.code === 'admin');
        if (isCurrentlyAdmin && !actorRoles.includes('admin')) {
          throw new AppError(
            'FORBIDDEN',
            403,
            'Chỉ quản trị viên (admin) mới có quyền mở khóa tài khoản của một admin.',
          );
        }

        // Người không phải admin chỉ mở khóa được người có quyền nằm gọn trong quyền của mình.
        assertActorCanManageTargetRoles(actorRoles, actorPermissions, targetUser.roles);

        // lockedAt về null nghĩa là không bị khóa; không cần xóa phiên vì lúc khóa phiên đã bị xóa hết.
        await tx.user.update({
          where: { id: targetUserId },
          data: { lockedAt: null },
        });

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
