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
 * Đổi mật khẩu tài khoản người dùng.
 */
export async function changePassword(params: ChangePasswordParams): Promise<void> {
  const { userId, currentPassword, newPassword } = params;

  // Độ dài tối thiểu 8 ký tự để bảo đảm entropy cơ bản chống tấn công brute-force.
  if (newPassword.length < 8) {
    throw new AppError('WEAK_PASSWORD', 400, 'Mật khẩu mới phải chứa tối thiểu 8 ký tự.');
  }

  const user = await db.user.findUniqueOrThrow({
    where: { id: userId },
  });

  // Tài khoản đăng ký bằng OAuth không có passwordHash thì bỏ qua bước xác minh mật khẩu cũ.
  if (user.passwordHash) {
    if (!currentPassword) {
      throw new AppError('INVALID_CREDENTIALS', 400, 'Vui lòng cung cấp mật khẩu hiện tại.');
    }
    const isMatch = await verifyPassword(user.passwordHash, currentPassword);
    if (!isMatch) {
      throw new AppError('INVALID_CREDENTIALS', 400, 'Mật khẩu hiện tại không chính xác.');
    }
  }

  // Băm bằng Argon2id trước khi vào transaction để giảm thời gian giữ transaction lock trong DB.
  const newHash = await hashPassword(newPassword);

  await db.$transaction(async (tx) => {
    await tx.user.update({
      where: { id: userId },
      data: { passwordHash: newHash },
    });

    // Đăng xuất toàn bộ thiết bị cũ nhằm ngăn chặn kẻ tấn công tiếp tục dùng phiên đã đánh cắp.
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
 * Xóa và ẩn danh hóa tài khoản khách hàng theo yêu cầu GDPR và bảo vệ dữ liệu.
 */
export async function deleteCustomerAccount(params: DeleteAccountParams): Promise<void> {
  const { userId, password } = params;

  const user = await db.user.findUnique({
    where: { id: userId },
    include: { oauthAccounts: true },
  });

  // Không cho phép thao tác trên tài khoản đã bị khóa hoặc đã xóa từ trước.
  if (!user || user.lockedAt || user.deletedAt) {
    throw new AppError('USER_NOT_FOUND', 404, 'Tài khoản không tồn tại hoặc đã bị khóa.');
  }

  // Chặn xóa nhầm tài khoản nhân viên qua luồng khách hàng vì nhân viên phải do admin quản lý.
  if (user.kind !== 'customer') {
    throw new AppError(
      'FORBIDDEN',
      403,
      'Chỉ tài khoản khách hàng mới có thể xóa qua chức năng này.',
    );
  }

  // Bắt buộc xác thực lại (re-authentication) để chống tấn công chiếm phiên thực hiện hành động phá hoại.
  if (user.passwordHash) {
    if (!password) {
      throw new AppError('UNAUTHORIZED', 401, 'Vui lòng nhập mật khẩu để xác nhận xóa tài khoản.');
    }
    const isMatch = await verifyPassword(user.passwordHash, password);
    if (!isMatch) {
      throw new AppError('INVALID_CREDENTIALS', 401, 'Mật khẩu xác nhận không chính xác.');
    }
  } else if (user.oauthAccounts.length > 0) {
    // Tài khoản không có mật khẩu chỉ cho phép xóa nếu vừa đăng nhập Google trong 5 phút để xác thực chính chủ.
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

  // Giữ lại bản ghi nhưng ẩn danh email và tên để không làm đứt gãy khóa ngoại của các đơn hàng lịch sử.
  const anonymizedEmail = `deleted-${user.id}@deleted.pickory.local`;
  const anonymizedName = 'Người dùng đã xóa';

  await db.$transaction(async (tx) => {
    // Thu hồi toàn bộ quyền truy cập và liên kết xác thực để vô hiệu hóa tài khoản vĩnh viễn.
    await tx.emailToken.deleteMany({ where: { userId: user.id } });
    await tx.oAuthAccount.deleteMany({ where: { userId: user.id } });
    await tx.session.deleteMany({ where: { userId: user.id } });

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

  // Bắn sự kiện để các module khác (như giỏ hàng, thông báo) chủ động dọn dẹp dữ liệu liên quan.
  await emitUserDeleted(user.id);
}
