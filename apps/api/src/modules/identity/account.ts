import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { emitUserDeleted } from './events';
import { hashPassword, verifyPassword } from './password';

export interface UpdateProfileParams {
  userId: string;
  name?: string | null;
  phone?: string | null;
}

/**
 * Cập nhật thông tin cá nhân (tên, số điện thoại).
 */
export async function updateProfile(params: UpdateProfileParams) {
  const { userId, name, phone } = params;

  const updated = await db.user.update({
    where: { id: userId },
    data: {
      name:
        name !== undefined ? (name === null || name.trim() === '' ? null : name.trim()) : undefined,
      phone:
        phone !== undefined
          ? phone === null || phone.trim() === ''
            ? null
            : phone.trim()
          : undefined,
    },
  });

  return {
    id: updated.id,
    email: updated.email,
    name: updated.name,
    phone: updated.phone,
    kind: updated.kind,
  };
}

export interface ChangePasswordParams {
  userId: string;
  currentPassword?: string;
  newPassword: string;
}

/**
 * Đổi mật khẩu tài khoản:
 * 1. Kiểm tra mật khẩu hiện tại (nếu tài khoản có đặt mật khẩu).
 * 2. Cập nhật mật khẩu mới (Argon2id, tối thiểu 8 ký tự).
 * 3. Xóa MỌI phiên đăng nhập của người dùng.
 */
export async function changePassword(params: ChangePasswordParams): Promise<void> {
  const { userId, currentPassword, newPassword } = params;

  if (newPassword.length < 8) {
    throw new AppError('WEAK_PASSWORD', 400, 'Mật khẩu mới phải chứa tối thiểu 8 ký tự.');
  }

  const user = await db.user.findUniqueOrThrow({
    where: { id: userId },
  });

  if (user.passwordHash) {
    if (!currentPassword) {
      throw new AppError('INVALID_CREDENTIALS', 400, 'Vui lòng cung cấp mật khẩu hiện tại.');
    }
    const isMatch = await verifyPassword(user.passwordHash, currentPassword);
    if (!isMatch) {
      throw new AppError('INVALID_CREDENTIALS', 400, 'Mật khẩu hiện tại không chính xác.');
    }
  }

  const newHash = await hashPassword(newPassword);

  await db.$transaction(async (tx) => {
    await tx.user.update({
      where: { id: userId },
      data: { passwordHash: newHash },
    });

    // Xóa mọi phiên cũ
    await tx.session.deleteMany({
      where: { userId },
    });
  });
}

export interface DeleteAccountParams {
  userId: string;
  password?: string;
  sessionCreatedAt?: Date;
}

/**
 * Xóa tài khoản khách:
 * 1. Bắt buộc xác nhận lại bằng mật khẩu (hoặc tài khoản liên kết OAuth).
 * 2. Ẩn danh email và tên, đặt deletedAt.
 * 3. Xóa credential, oauth_accounts, email_tokens, sessions.
 * 4. Phát UserDeletedEvent { userId } để các module khác dọn dữ liệu.
 */
export async function deleteCustomerAccount(params: DeleteAccountParams): Promise<void> {
  const { userId, password } = params;

  const user = await db.user.findUnique({
    where: { id: userId },
    include: { oauthAccounts: true },
  });

  if (!user || user.lockedAt || user.deletedAt) {
    throw new AppError('USER_NOT_FOUND', 404, 'Tài khoản không tồn tại hoặc đã bị khóa.');
  }

  if (user.kind !== 'customer') {
    throw new AppError(
      'FORBIDDEN',
      403,
      'Chỉ tài khoản khách hàng mới có thể xóa qua chức năng này.',
    );
  }

  // Xác nhận lại danh tính trước khi xóa
  if (user.passwordHash) {
    if (!password) {
      throw new AppError('UNAUTHORIZED', 401, 'Vui lòng nhập mật khẩu để xác nhận xóa tài khoản.');
    }
    const isMatch = await verifyPassword(user.passwordHash, password);
    if (!isMatch) {
      throw new AppError('INVALID_CREDENTIALS', 401, 'Mật khẩu xác nhận không chính xác.');
    }
  } else if (user.oauthAccounts.length > 0) {
    // Tài khoản Google-only: yêu cầu đăng nhập lại Google trong 5 phút gần nhất
    const now = Date.now();
    const sessionAgeMs = params.sessionCreatedAt
      ? now - params.sessionCreatedAt.getTime()
      : Infinity;
    const FIVE_MINUTES_MS = 5 * 60 * 1000;
    if (sessionAgeMs > FIVE_MINUTES_MS) {
      throw new AppError(
        'REAUTH_REQUIRED',
        401,
        'Vui lòng đăng nhập lại Google trong vòng 5 phút để xác nhận xóa tài khoản.',
      );
    }
  } else {
    throw new AppError('UNAUTHORIZED', 401, 'Thiếu phương thức xác nhận tài khoản.');
  }

  const anonymizedEmail = `deleted-${user.id}@deleted.pickory.local`;
  const anonymizedName = 'Người dùng đã xóa';

  await db.$transaction(async (tx) => {
    // Xóa liên kết token, OAuth, sessions
    await tx.emailToken.deleteMany({ where: { userId: user.id } });
    await tx.oAuthAccount.deleteMany({ where: { userId: user.id } });
    await tx.session.deleteMany({ where: { userId: user.id } });

    // Ẩn danh hóa người dùng và đặt deletedAt
    await tx.user.update({
      where: { id: user.id },
      data: {
        email: anonymizedEmail,
        name: anonymizedName,
        phone: null,
        passwordHash: null,
        deletedAt: new Date(),
      },
    });
  });

  // Phát đúng 1 sự kiện UserDeletedEvent
  await emitUserDeleted(user.id);
}
