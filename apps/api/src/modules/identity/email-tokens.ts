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
  // Chỉ lưu SHA-256 hash của token vào DB để phòng ngừa lộ token nếu DB bị rò rỉ.
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

    // Dùng updateMany kèm điều kiện usedAt: null để tiêu thụ token nguyên tử (atomic), chống race condition khi bị click đồng thời.
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
 * Xử lý yêu cầu quên mật khẩu và gửi email khôi phục tài khoản nếu email tồn tại.
 */
export async function forgotPassword(
  email: string,
  ip = '127.0.0.1',
  now = Date.now(),
): Promise<void> {
  const normalizedEmail = email.trim().toLowerCase();
  const normalizedIp = ip.trim();

  // Áp dụng giới hạn tần suất kép theo cả email và IP để chống càn quét gửi thư rác và làm nghẽn dịch vụ gửi email.
  const isEmailLimited = forgotPasswordEmailLimiter.isRateLimited(normalizedEmail, now);
  const isIpLimited = forgotPasswordIpLimiter.isRateLimited(normalizedIp, now);

  // Ghi nhận lần gọi cho cả email và IP
  forgotPasswordEmailLimiter.recordAttempt(normalizedEmail, now);
  forgotPasswordIpLimiter.recordAttempt(normalizedIp, now);

  if (isEmailLimited || isIpLimited) {
    // Khi bị rate limit vẫn tính toán hash giả để thời gian phản hồi tương đương luồng thường, tránh lộ thông tin qua timing attack.
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
      // Giới hạn hiệu lực 30 phút để thu hẹp khung thời gian rủi ro nếu đường dẫn trong email bị lộ.
      expiresInMs: 30 * 60 * 1000,
    });

    const config = loadConfig();
    await getEmailSender().send({
      to: user.email,
      subject: 'Đặt lại mật khẩu Pickory',
      text: `Chào bạn, vui lòng truy cập liên kết sau để đặt lại mật khẩu: ${config.WEB_ORIGIN}/reset-password?token=${token}`,
      metadata: { token, purpose: 'reset' },
    });
  } else {
    // Chạy hash giả lập khi không tìm thấy tài khoản để cân bằng độ trễ với nhánh có tài khoản, chống timing attack dò email.
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
 * Cập nhật mật khẩu mới thông qua mã xác thực từ email đặt lại mật khẩu hoặc thư mời.
 */
export async function resetPassword(params: ResetPasswordParams): Promise<ResetPasswordResult> {
  const { token, newPassword } = params;
  // Yêu cầu mật khẩu tối thiểu 8 ký tự để bảo đảm an toàn dữ liệu người dùng.
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

    // Tiêu thụ token nguyên tử (atomic) bằng điều kiện usedAt: null để ngăn chặn tấn công phát lại (replay attack) cùng lúc.
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
        // Xác thực email luôn tại đây vì người dùng đã chứng minh quyền sở hữu hòm thư thông qua việc nhận token.
        emailVerified: true,
      },
    });

    // Hủy toàn bộ token khôi phục và lời mời đang chờ khác để ngăn chặn việc tái sử dụng các đường dẫn cũ.
    await tx.emailToken.updateMany({
      where: {
        userId: record.userId,
        id: { not: record.id },
        usedAt: null,
        purpose: { in: ['reset', 'invite'] },
      },
      data: { usedAt: now },
    });

    // Thu hồi mọi phiên đăng nhập đang hoạt động để buộc tất cả thiết bị phải xác thực lại bằng mật khẩu mới.
    await tx.session.deleteMany({
      where: { userId: record.userId },
    });

    return {
      userKind: record.user.kind,
    };
  });
}
