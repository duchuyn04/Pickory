import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { emitUserDeleted } from './events';
import { hashPassword, verifyPassword } from './password';

export interface UpdateProfileParams {
  userId: string;
  name?: string | null;
  phone?: string | null;
}

// Sửa tên và số điện thoại của chính người dùng.
export async function updateProfile(params: UpdateProfileParams) {
  const { userId, name, phone } = params;

  const updated = await db.user.update({
    where: { id: userId },
    data: {
      // undefined nghĩa là không gửi trường này nên giữ nguyên; null hoặc chuỗi rỗng nghĩa là người dùng muốn xóa giá trị.
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

  // Chỉ trả các trường an toàn, không bao giờ trả passwordHash.
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

// Đổi mật khẩu và đăng xuất mọi thiết bị.
export async function changePassword(params: ChangePasswordParams): Promise<void> {
  const { userId, currentPassword, newPassword } = params;

  // Kiểm tra lại độ dài ở đây dù route đã kiểm, vì hàm này có thể được gọi từ nơi khác.
  if (newPassword.length < 8) {
    throw new AppError('WEAK_PASSWORD', 400, 'Mật khẩu mới phải chứa tối thiểu 8 ký tự.');
  }

  const user = await db.user.findUniqueOrThrow({
    where: { id: userId },
  });

  // Tài khoản chỉ dùng Google chưa có mật khẩu nên không có gì để đối chiếu; chỉ tài khoản có mật khẩu mới phải nhập mật khẩu cũ.
  if (user.passwordHash) {
    // Bắt nhập mật khẩu cũ để người ngồi nhầm máy đang đăng nhập không đổi được mật khẩu của chủ tài khoản.
    if (!currentPassword) {
      throw new AppError('INVALID_CREDENTIALS', 400, 'Vui lòng cung cấp mật khẩu hiện tại.');
    }
    const isMatch = await verifyPassword(user.passwordHash, currentPassword);
    if (!isMatch) {
      throw new AppError('INVALID_CREDENTIALS', 400, 'Mật khẩu hiện tại không chính xác.');
    }
  }

  // Băm trước khi mở transaction vì Argon2 chậm, không nên giữ transaction mở trong lúc băm.
  const newHash = await hashPassword(newPassword);

  await db.$transaction(async (tx) => {
    await tx.user.update({
      where: { id: userId },
      data: { passwordHash: newHash },
    });

    // Xóa mọi phiên (kể cả phiên hiện tại): nếu mật khẩu cũ đã bị lộ thì kẻ đang đăng nhập cũng bị đá ra.
    // Làm cùng transaction để không xảy ra trường hợp đổi mật khẩu xong mà phiên cũ còn sống.
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

// Xóa tài khoản khách: ẩn danh dữ liệu cá nhân nhưng giữ bản ghi để đơn hàng cũ vẫn tham chiếu đúng.
export async function deleteCustomerAccount(params: DeleteAccountParams): Promise<void> {
  const { userId, password } = params;

  const user = await db.user.findUnique({
    where: { id: userId },
    include: { oauthAccounts: true },
  });

  // Tài khoản không tồn tại, đã bị khóa hoặc đã xóa thì không xử lý tiếp.
  if (!user || user.lockedAt || user.deletedAt) {
    throw new AppError('USER_NOT_FOUND', 404, 'Tài khoản không tồn tại hoặc đã bị khóa.');
  }

  // Nhân viên do admin quản lý (khóa, không xóa), nên luồng tự xóa này chỉ dành cho khách.
  if (user.kind !== 'customer') {
    throw new AppError(
      'FORBIDDEN',
      403,
      'Chỉ tài khoản khách hàng mới có thể xóa qua chức năng này.',
    );
  }

  // Xóa là thao tác không đảo ngược nên phải xác nhận lại danh tính, phòng trường hợp phiên đang đăng nhập bị người khác dùng.
  if (user.passwordHash) {
    if (!password) {
      throw new AppError('UNAUTHORIZED', 401, 'Vui lòng nhập mật khẩu để xác nhận xóa tài khoản.');
    }
    const isMatch = await verifyPassword(user.passwordHash, password);
    if (!isMatch) {
      throw new AppError('INVALID_CREDENTIALS', 401, 'Mật khẩu xác nhận không chính xác.');
    }
  } else if (user.oauthAccounts.length > 0) {
    // Tài khoản chỉ dùng Google không có mật khẩu để xác nhận, nên coi việc vừa đăng nhập Google gần đây là bằng chứng chính chủ.
    const now = Date.now();
    // Thiếu thời điểm tạo phiên thì coi như phiên quá cũ (Infinity), để mặc định là từ chối chứ không phải cho qua.
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
    // Không có mật khẩu lẫn liên kết Google thì không có cách xác nhận nào, từ chối.
    throw new AppError('UNAUTHORIZED', 401, 'Thiếu phương thức xác nhận tài khoản.');
  }

  // Email ẩn danh chứa id để vẫn duy nhất (cột email là unique), và để email gốc được giải phóng.
  const anonymizedEmail = `deleted-${user.id}@deleted.pickory.local`;
  const anonymizedName = 'Người dùng đã xóa';

  await db.$transaction(async (tx) => {
    // Xóa token email, liên kết Google và phiên: không còn cách nào đăng nhập hay khôi phục tài khoản này.
    await tx.emailToken.deleteMany({ where: { userId: user.id } });
    await tx.oAuthAccount.deleteMany({ where: { userId: user.id } });
    await tx.session.deleteMany({ where: { userId: user.id } });

    // Không xóa dòng users mà chỉ ẩn danh và đánh dấu deletedAt, vì đơn hàng cũ còn khóa ngoại tới dòng này.
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

  // Phát sự kiện sau khi transaction thành công, để các module khác xóa dữ liệu riêng của người dùng này.
  await emitUserDeleted(user.id);
}
