import type { EmailTokenPurpose, Prisma, PrismaClient } from '../../generated/prisma/client';
import { loadConfig } from '../../shared/config';
import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { getEmailSender } from './email-sender';
import { hashPassword } from './password';
import { forgotPasswordEmailLimiter, forgotPasswordIpLimiter } from './rate-limit';
import { generateToken, hashToken } from './tokens';

export interface CreateEmailTokenParams {
  userId: string;
  purpose: EmailTokenPurpose;
  expiresInMs: number;
}

/**
 * Tạo token một lần lưu hash SHA-256 vào database, trả về token gốc để gửi email.
 */
export async function createEmailToken(
  params: CreateEmailTokenParams,
  client: PrismaClient | Prisma.TransactionClient = db,
): Promise<string> {
  const { userId, purpose, expiresInMs } = params;
  const token = generateToken();
  const tokenHash = hashToken(token);
  const expiresAt = new Date(Date.now() + expiresInMs);

  await client.emailToken.create({
    data: {
      tokenHash,
      userId,
      purpose,
      expiresAt,
    },
  });

  return token;
}

/**
 * Kiểm tra tính hợp lệ của token (chưa dùng, chưa hết hạn, đúng mục đích).
 */
export async function verifyEmailToken(token: string, purpose: EmailTokenPurpose) {
  const tokenHash = hashToken(token);
  const record = await db.emailToken.findUnique({
    where: { tokenHash },
    include: { user: true },
  });

  const now = new Date();
  if (!record || record.purpose !== purpose || record.usedAt !== null || record.expiresAt <= now) {
    throw new AppError(
      'INVALID_TOKEN',
      400,
      'Liên kết không hợp lệ, đã hết hạn hoặc đã được sử dụng.',
    );
  }

  return record;
}

/**
 * Đánh dấu token đã được sử dụng.
 */
export async function markTokenUsed(tokenId: string): Promise<void> {
  await db.emailToken.update({
    where: { id: tokenId },
    data: { usedAt: new Date() },
  });
}

/**
 * Xác thực email của người dùng. Link xác thực có hạn 24 giờ.
 */
export async function verifyEmail(token: string): Promise<void> {
  const tokenHash = hashToken(token);
  const now = new Date();

  await db.$transaction(async (tx) => {
    const record = await tx.emailToken.findUnique({
      where: { tokenHash },
    });

    if (
      !record ||
      record.purpose !== 'verify' ||
      record.usedAt !== null ||
      record.expiresAt <= now
    ) {
      throw new AppError(
        'INVALID_TOKEN',
        400,
        'Liên kết không hợp lệ, đã hết hạn hoặc đã được sử dụng.',
      );
    }

    const updated = await tx.emailToken.updateMany({
      where: { id: record.id, usedAt: null, expiresAt: { gt: now } },
      data: { usedAt: now },
    });

    if (updated.count !== 1) {
      throw new AppError(
        'INVALID_TOKEN',
        400,
        'Liên kết không hợp lệ, đã hết hạn hoặc đã được sử dụng.',
      );
    }

    await tx.user.update({
      where: { id: record.userId },
      data: { emailVerified: true },
    });
  });
}

/**
 * Quên mật khẩu: gửi link đặt lại mật khẩu hạn 30 phút, dùng 1 lần.
 * Luôn trả cùng một thông báo và độ trễ xấp xỉ nhau dù email có tồn tại hay không (chống dò tài khoản).
 */
export async function forgotPassword(
  email: string,
  ip = '127.0.0.1',
  now = Date.now(),
): Promise<void> {
  const normalizedEmail = email.trim().toLowerCase();
  const normalizedIp = ip.trim();

  // Kiểm tra giới hạn tần suất: 3 lần / 15 phút / email và 10 lần / 1 giờ / IP
  const isEmailLimited = forgotPasswordEmailLimiter.isRateLimited(normalizedEmail, now);
  const isIpLimited = forgotPasswordIpLimiter.isRateLimited(normalizedIp, now);

  // Ghi nhận lần gọi cho cả email và IP
  forgotPasswordEmailLimiter.recordAttempt(normalizedEmail, now);
  forgotPasswordIpLimiter.recordAttempt(normalizedIp, now);

  if (isEmailLimited || isIpLimited) {
    // Vượt ngưỡng: giữ phản hồi chung, không sinh token và không gửi email (chống dò email)
    await hashPassword('dummy-password-timing-defense');
    return;
  }

  const user = await db.user.findUnique({
    where: { email: normalizedEmail },
  });
  if (user && !user.lockedAt && !user.deletedAt) {
    const token = await createEmailToken({
      userId: user.id,
      purpose: 'reset',
      expiresInMs: 30 * 60 * 1000, // 30 phút
    });

    const config = loadConfig();
    await getEmailSender().send({
      to: user.email,
      subject: 'Đặt lại mật khẩu Pickory',
      text: `Chào bạn, vui lòng truy cập liên kết sau để đặt lại mật khẩu: ${config.WEB_ORIGIN}/reset-password?token=${token}`,
      metadata: { token, purpose: 'reset' },
    });
  } else {
    // Độ trễ giả lập tương đương để chống timing attack
    await hashPassword('dummy-password-timing-defense');
  }
}

export interface ResetPasswordParams {
  token: string;
  newPassword: string;
}

export interface ResetPasswordResult {
  userKind: 'customer' | 'staff';
}

/**
 * Đặt mật khẩu mới bằng token:
 * 1. Kiểm tra hạn dùng (30 phút với reset, 72 giờ với invite) và dùng 1 lần.
 * 2. Đổi passwordHash mới (Argon2id).
 * 3. Xóa MỌI phiên cũ của người dùng trong DB (bắt buộc đăng nhập lại).
 * 4. Đánh dấu token đã dùng (atomic để chống race condition).
 * 5. Vô hiệu hóa mọi token reset/invite khác còn hạn của user này.
 */
export async function resetPassword(params: ResetPasswordParams): Promise<ResetPasswordResult> {
  const { token, newPassword } = params;
  if (newPassword.length < 8) {
    throw new AppError('WEAK_PASSWORD', 400, 'Mật khẩu phải chứa tối thiểu 8 ký tự.');
  }

  const tokenHash = hashToken(token);
  const now = new Date();

  return db.$transaction(async (tx) => {
    const record = await tx.emailToken.findUnique({
      where: { tokenHash },
      include: { user: true },
    });

    if (
      !record ||
      (record.purpose !== 'reset' && record.purpose !== 'invite') ||
      record.usedAt !== null ||
      record.expiresAt <= now
    ) {
      throw new AppError(
        'INVALID_TOKEN',
        400,
        'Liên kết đặt lại mật khẩu không hợp lệ, đã hết hạn hoặc đã được sử dụng.',
      );
    }

    // Tiêu thụ token nguyên tử
    const updated = await tx.emailToken.updateMany({
      where: { id: record.id, usedAt: null, expiresAt: { gt: now } },
      data: { usedAt: now },
    });

    if (updated.count !== 1) {
      throw new AppError(
        'INVALID_TOKEN',
        400,
        'Liên kết đặt lại mật khẩu không hợp lệ, đã hết hạn hoặc đã được sử dụng.',
      );
    }

    const passwordHash = await hashPassword(newPassword);

    await tx.user.update({
      where: { id: record.userId },
      data: {
        passwordHash,
        emailVerified: true,
      },
    });

    // Vô hiệu hóa mọi token reset/invite khác còn hạn của user này
    await tx.emailToken.updateMany({
      where: {
        userId: record.userId,
        id: { not: record.id },
        usedAt: null,
        purpose: { in: ['reset', 'invite'] },
      },
      data: { usedAt: now },
    });

    // Xóa MỌI phiên cũ của người dùng này
    await tx.session.deleteMany({
      where: { userId: record.userId },
    });

    return {
      userKind: record.user.kind,
    };
  });
}
