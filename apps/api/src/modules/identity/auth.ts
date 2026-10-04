import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { getEmailSender } from './email-sender';
import { emitUserLinked } from './events';
import { loginThrottle } from './login-throttle';
import { hashPassword, verifyPassword } from './password';
import { generateToken, hashToken } from './tokens';

const DUMMY_HASH =
  '$argon2id$v=19$m=19456,t=2,p=1$Q29uc3RhbnRUaW1lU2FsdA$uF7hN93Qx2Tq89jFp1m5A0/X84f/7Dk06u6jJ7uV6U4';

export interface StaffLoginParams {
  email: string;
  password: string;
  ip: string;
}

export interface StaffLoginResult {
  user: {
    id: string;
    email: string;
    name: string | null;
    kind: 'staff';
  };
  token: string;
  expiresAt: Date;
}

/**
 * Xác thực và tạo phiên làm việc cho nhân viên nội bộ.
 */
export async function staffLogin(params: StaffLoginParams): Promise<StaffLoginResult> {
  const { email, password, ip } = params;
  const normalizedEmail = email.trim().toLowerCase();

  // Kiểm tra giới hạn số lần thử theo IP và email để chặn các đợt tấn công brute-force mật khẩu.
  loginThrottle.assertNotLocked(normalizedEmail, ip);

  const user = await db.user.findUnique({
    where: { email: normalizedEmail },
  });

  if (
    !user ||
    user.kind !== 'staff' ||
    user.lockedAt !== null ||
    user.deletedAt !== null ||
    !user.passwordHash
  ) {
    // Chạy verify với hash giả lập để thời gian phản hồi đồng nhất (constant time), chống tấn công timing attack dò email tồn tại.
    await verifyPassword(DUMMY_HASH, password).catch(() => false);
    // Ghi nhận lần thử sai để tăng dần bộ đếm throttle cho cặp email và IP hiện tại.
    loginThrottle.recordFailure(normalizedEmail, ip);
    throw new AppError('INVALID_CREDENTIALS', 401, 'Email hoặc mật khẩu không chính xác.');
  }

  const isValidPassword = await verifyPassword(user.passwordHash, password);
  if (!isValidPassword) {
    loginThrottle.recordFailure(normalizedEmail, ip);
    throw new AppError('INVALID_CREDENTIALS', 401, 'Email hoặc mật khẩu không chính xác.');
  }

  // Xóa bộ đếm số lần sai sau khi đăng nhập thành công để không ảnh hưởng đến các lần đăng nhập hợp lệ tiếp theo.
  loginThrottle.recordSuccess(normalizedEmail, ip);

  const token = generateToken();
  const tokenHash = hashToken(token);
  // Nhân viên nội bộ chỉ được cấp phiên tối đa 12 giờ không gia hạn tự động để giảm thiểu rủi ro khi quên đăng xuất trên máy công ty.
  const expiresAt = new Date(Date.now() + 12 * 60 * 60 * 1000);

  // Chỉ lưu SHA-256 hash của session token vào DB để nếu DB bị rò rỉ thì kẻ tấn công cũng không thể giả mạo phiên.
  await db.session.create({
    data: {
      tokenHash,
      userId: user.id,
      expiresAt,
    },
  });

  return {
    user: {
      id: user.id,
      email: user.email,
      name: user.name,
      kind: 'staff',
    },
    token,
    expiresAt,
  };
}

export interface CustomerRegisterParams {
  email: string;
  password: string;
  name?: string;
  phone?: string;
  anonymousId: string;
  ip: string;
}

export interface AuthResult {
  user: {
    id: string;
    email: string;
    name: string | null;
    phone: string | null;
    kind: 'customer';
  };
  token: string;
  expiresAt: Date;
}

/**
 * Đăng ký tài khoản khách hàng mới kèm khởi tạo phiên đăng nhập ban đầu.
 */
export async function customerRegister(params: CustomerRegisterParams): Promise<AuthResult> {
  const { email, password, name, phone, anonymousId } = params;
  const normalizedEmail = email.trim().toLowerCase();

  // Quy định độ dài tối thiểu 8 ký tự để bảo đảm mức an toàn cơ bản cho mật khẩu người dùng.
  if (password.length < 8) {
    throw new AppError('WEAK_PASSWORD', 400, 'Mật khẩu phải chứa tối thiểu 8 ký tự.');
  }

  const existingUser = await db.user.findUnique({
    where: { email: normalizedEmail },
  });

  // Email là định danh duy nhất cho mỗi tài khoản trong toàn hệ thống.
  if (existingUser) {
    throw new AppError(
      'EMAIL_ALREADY_EXISTS',
      400,
      'Email này đã được sử dụng. Vui lòng đăng nhập hoặc chọn email khác.',
    );
  }

  const customerRole = await db.role.findUnique({
    where: { code: 'customer' },
  });
  // Role customer là bắt buộc để phân quyền các tính năng mua sắm của khách.
  if (!customerRole) {
    throw new Error('Role customer chưa được seed trong database');
  }

  const passwordHash = await hashPassword(password);

  const newUser = await db.user.create({
    data: {
      email: normalizedEmail,
      emailVerified: false,
      passwordHash,
      name: name?.trim() || null,
      phone: phone?.trim() || null,
      kind: 'customer',
      roles: {
        create: {
          roleId: customerRole.id,
        },
      },
    },
  });

  // Cấp token xác thực có hạn 24 giờ để người dùng kịp kích hoạt tài khoản qua hòm thư cá nhân.
  const verifyToken = generateToken();
  const verifyTokenHash = hashToken(verifyToken);
  const verifyExpiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000);

  await db.emailToken.create({
    data: {
      tokenHash: verifyTokenHash,
      userId: newUser.id,
      purpose: 'verify',
      expiresAt: verifyExpiresAt,
    },
  });

  await getEmailSender().send({
    to: newUser.email,
    subject: 'Xác thực tài khoản Pickory',
    text: `Chào bạn, vui lòng truy cập link sau để xác thực tài khoản: /verify-email?token=${verifyToken}`,
    metadata: { token: verifyToken, purpose: 'verify' },
  });

  // Khách hàng được duy trì phiên dài 30 ngày để tối ưu trải nghiệm mua sắm không phải đăng nhập liên tục.
  const sessionToken = generateToken();
  const sessionTokenHash = hashToken(sessionToken);
  const sessionExpiresAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000);

  await db.session.create({
    data: {
      tokenHash: sessionTokenHash,
      userId: newUser.id,
      expiresAt: sessionExpiresAt,
    },
  });

  // Liên kết danh tính ẩn danh trước đó với tài khoản mới tạo để hợp nhất giỏ hàng và dữ liệu sự kiện.
  await emitUserLinked(anonymousId, newUser.id);

  return {
    user: {
      id: newUser.id,
      email: newUser.email,
      name: newUser.name,
      phone: newUser.phone,
      kind: 'customer',
    },
    token: sessionToken,
    expiresAt: sessionExpiresAt,
  };
}

export interface CustomerLoginParams {
  email: string;
  password: string;
  anonymousId: string;
  ip: string;
}

/**
 * Xác thực tài khoản khách hàng và gắn phiên làm việc mới.
 */
export async function customerLogin(params: CustomerLoginParams): Promise<AuthResult> {
  const { email, password, anonymousId, ip } = params;
  const normalizedEmail = email.trim().toLowerCase();

  // Kiểm tra giới hạn số lần thử theo IP và email để chặn brute-force mật khẩu khách hàng.
  loginThrottle.assertNotLocked(normalizedEmail, ip);

  const user = await db.user.findUnique({
    where: { email: normalizedEmail },
  });

  if (
    !user ||
    user.kind !== 'customer' ||
    user.lockedAt !== null ||
    user.deletedAt !== null ||
    !user.passwordHash
  ) {
    // Chạy hash giả lập để giữ thời gian xử lý đồng nhất (constant time), tránh bị dò đoán email có tồn tại hay không.
    await verifyPassword(DUMMY_HASH, password).catch(() => false);
    loginThrottle.recordFailure(normalizedEmail, ip);
    throw new AppError('INVALID_CREDENTIALS', 401, 'Email hoặc mật khẩu không chính xác.');
  }

  const isValidPassword = await verifyPassword(user.passwordHash, password);
  if (!isValidPassword) {
    loginThrottle.recordFailure(normalizedEmail, ip);
    throw new AppError('INVALID_CREDENTIALS', 401, 'Email hoặc mật khẩu không chính xác.');
  }

  loginThrottle.recordSuccess(normalizedEmail, ip);

  // Phiên đăng nhập khách hàng kéo dài 30 ngày để giữ trạng thái đăng nhập thuận tiện khi duyệt web.
  const token = generateToken();
  const tokenHash = hashToken(token);
  const expiresAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000);

  await db.session.create({
    data: {
      tokenHash,
      userId: user.id,
      expiresAt,
    },
  });

  // Gắn kết phiên ẩn danh hiện tại với tài khoản người dùng vừa đăng nhập nhằm đồng bộ giỏ hàng khách vãng lai.
  await emitUserLinked(anonymousId, user.id);

  return {
    user: {
      id: user.id,
      email: user.email,
      name: user.name,
      phone: user.phone,
      kind: 'customer',
    },
    token,
    expiresAt,
  };
}

/**
 * Hủy bỏ phiên làm việc của người dùng trong cơ sở dữ liệu.
 */
export async function logout(sessionToken?: string): Promise<void> {
  if (!sessionToken) return;
  // Hash token trước khi truy vấn vì DB chỉ lưu hash chứ không lưu token gốc dạng plain text.
  const tokenHash = hashToken(sessionToken);
  await db.session.deleteMany({
    where: { tokenHash },
  });
}
