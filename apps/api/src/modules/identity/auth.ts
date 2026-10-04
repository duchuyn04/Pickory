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
 * Đăng nhập nhân viên:
 * 1. Kiểm tra chống dò mật khẩu (throttle).
 * 2. Xác thực email + mật khẩu Argon2id.
 * 3. Tạo phiên 12 giờ không gia hạn, lưu tokenHash SHA-256 vào DB.
 * 4. Không phát UserLinkedEvent.
 */
export async function staffLogin(params: StaffLoginParams): Promise<StaffLoginResult> {
  const { email, password, ip } = params;
  const normalizedEmail = email.trim().toLowerCase();

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

  const token = generateToken();
  const tokenHash = hashToken(token);
  const expiresAt = new Date(Date.now() + 12 * 60 * 60 * 1000);

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
 * Đăng ký tài khoản khách hàng:
 * 1. Mật khẩu tối thiểu 8 ký tự.
 * 2. Email đã thuộc nhân viên thì chặn.
 * 3. Gán role customer, tạo phiên 30 ngày.
 * 4. Gửi email xác thực.
 * 5. Phát UserLinkedEvent { anonymousId, userId }.
 */
export async function customerRegister(params: CustomerRegisterParams): Promise<AuthResult> {
  const { email, password, name, phone, anonymousId } = params;
  const normalizedEmail = email.trim().toLowerCase();

  if (password.length < 8) {
    throw new AppError('WEAK_PASSWORD', 400, 'Mật khẩu phải chứa tối thiểu 8 ký tự.');
  }

  const existingUser = await db.user.findUnique({
    where: { email: normalizedEmail },
  });

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

  // Tạo email token xác thực (hạn 24 giờ) và gửi email qua EmailSender
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

  // Tạo phiên 30 ngày cho khách
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

  // Phát UserLinkedEvent { anonymousId, userId }
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
 * Đăng nhập khách hàng:
 * 1. Kiểm tra throttle.
 * 2. Xác thực Argon2id.
 * 3. Tạo phiên 30 ngày.
 * 4. Phát UserLinkedEvent { anonymousId, userId }.
 */
export async function customerLogin(params: CustomerLoginParams): Promise<AuthResult> {
  const { email, password, anonymousId, ip } = params;
  const normalizedEmail = email.trim().toLowerCase();

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

  // Phát UserLinkedEvent { anonymousId, userId }
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
 * Đăng xuất: xóa phiên trong DB.
 */
export async function logout(sessionToken?: string): Promise<void> {
  if (!sessionToken) return;
  const tokenHash = hashToken(sessionToken);
  await db.session.deleteMany({
    where: { tokenHash },
  });
}
