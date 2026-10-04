import type { EmailTokenPurpose, Prisma, PrismaClient } from '../../../generated/prisma/client';
import { loadConfig } from '../../../shared/config';
import { db } from '../../../shared/db';
import { AppError } from '../../../shared/errors';
import { getEmailSender } from '../data/email-sender';
import { hashPassword } from '../security/password';
import { forgotPasswordEmailLimiter, forgotPasswordIpLimiter } from '../security/rate-limit';
import { generateToken, hashToken } from '../security/tokens';

export interface CreateEmailTokenParams {
  userId: string;
  purpose: EmailTokenPurpose;
  expiresInMs: number;
}

// Tạo token dùng một lần cho link trong email; trả về token gốc để đưa vào thư.
export async function createEmailToken(
  params: CreateEmailTokenParams,
  // Nhận client để gọi được trong transaction của nơi khác (ví dụ mời nhân viên tạo user và token cùng lúc).
  client: PrismaClient | Prisma.TransactionClient = db,
): Promise<string> {
  const { userId, purpose, expiresInMs } = params;
  const token = generateToken();
  // DB chỉ giữ hash, nên lộ DB cũng không dùng được link.
  const tokenHash = hashToken(token);
  const expiresAt = new Date(Date.now() + expiresInMs);

  await client.emailToken.create({
    data: {
      tokenHash,
      userId,
      // purpose để token reset không dùng được vào việc xác thực email, và ngược lại.
      purpose,
      expiresAt,
    },
  });

  return token;
}

// Kiểm tra token còn dùng được không: đúng mục đích, chưa dùng, chưa hết hạn.
export async function verifyEmailToken(token: string, purpose: EmailTokenPurpose) {
  const tokenHash = hashToken(token);
  const record = await db.emailToken.findUnique({
    where: { tokenHash },
    include: { user: true },
  });

  const now = new Date();
  // Một thông báo chung cho mọi lý do hỏng, để người ngoài không phân biệt được token sai với token hết hạn hay đã dùng.
  if (!record || record.purpose !== purpose || record.usedAt !== null || record.expiresAt <= now) {
    throw new AppError(
      'INVALID_TOKEN',
      400,
      'Liên kết không hợp lệ, đã hết hạn hoặc đã được sử dụng.',
    );
  }

  return record;
}

// Đánh dấu token đã dùng để không dùng lại được.
export async function markTokenUsed(tokenId: string): Promise<void> {
  await db.emailToken.update({
    where: { id: tokenId },
    data: { usedAt: new Date() },
  });
}

// Xác thực email từ link trong thư (hạn 24 giờ).
export async function verifyEmail(token: string): Promise<void> {
  const tokenHash = hashToken(token);
  const now = new Date();

  // Dùng transaction để việc đánh dấu token đã dùng và việc xác thực email thành công hoặc hỏng cùng nhau.
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

    // Đánh dấu đã dùng bằng một lệnh có điều kiện usedAt: null; hai request đồng thời cùng đọc thấy token còn dùng được,
    // nhưng chỉ một request cập nhật được dòng này (count = 1), request kia thấy count = 0.
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

// Quên mật khẩu: gửi link đặt lại nếu email tồn tại, và luôn kết thúc theo cùng một cách dù tồn tại hay không.
export async function forgotPassword(
  email: string,
  ip = '127.0.0.1',
  // Cho test truyền thời gian giả.
  now = Date.now(),
): Promise<void> {
  const normalizedEmail = email.trim().toLowerCase();
  const normalizedIp = ip.trim();

  // Kiểm tra giới hạn theo cả email (không spam hòm thư một người) lẫn IP (không quét hàng loạt email).
  const isEmailLimited = forgotPasswordEmailLimiter.isRateLimited(normalizedEmail, now);
  const isIpLimited = forgotPasswordIpLimiter.isRateLimited(normalizedIp, now);

  // Tính lượt gọi cả khi đã bị chặn, để kẻ cố tình gọi liên tục không được mở lại sớm hơn.
  forgotPasswordEmailLimiter.recordAttempt(normalizedEmail, now);
  forgotPasswordIpLimiter.recordAttempt(normalizedIp, now);

  if (isEmailLimited || isIpLimited) {
    // Không báo lỗi 429, vì lỗi sẽ cho kẻ dò biết email đang bị gọi nhiều (nghĩa là có thể tồn tại); trả về như bình thường nhưng không gửi thư.
    // Băm một lần để thời gian phản hồi gần bằng nhánh có gửi thư.
    await hashPassword('dummy-password-timing-defense');
    return;
  }

  const user = await db.user.findUnique({
    where: { email: normalizedEmail },
  });
  // Tài khoản bị khóa hoặc đã xóa không được đặt lại mật khẩu để quay lại.
  if (user && !user.lockedAt && !user.deletedAt) {
    const token = await createEmailToken({
      userId: user.id,
      purpose: 'reset',
      // 30 phút theo spec, để link lọt vào tay người khác chỉ dùng được trong thời gian ngắn.
      expiresInMs: 30 * 60 * 1000,
    });

    const config = loadConfig();
    await getEmailSender().send({
      to: user.email,
      subject: 'Đặt lại mật khẩu Pickory',
      // Ghép WEB_ORIGIN vì link trong email phải là địa chỉ đầy đủ, đường dẫn tương đối không mở được từ hòm thư.
      text: `Chào bạn, vui lòng truy cập liên kết sau để đặt lại mật khẩu: ${config.WEB_ORIGIN}/reset-password?token=${token}`,
      metadata: { token, purpose: 'reset' },
    });
  } else {
    // Email không tồn tại cũng băm một lần để thời gian phản hồi giống nhánh có tài khoản, không lộ email nào đã đăng ký.
    await hashPassword('dummy-password-timing-defense');
  }
}

export interface ResetPasswordParams {
  token: string;
  newPassword: string;
}

export interface ResetPasswordResult {
  // Để giao diện biết đưa người dùng sang trang đăng nhập của khách hay của nhân viên.
  userKind: 'customer' | 'staff';
}

// Đặt mật khẩu mới bằng token; dùng cho cả link quên mật khẩu và link mời nhân viên.
export async function resetPassword(params: ResetPasswordParams): Promise<ResetPasswordResult> {
  const { token, newPassword } = params;
  // Kiểm tra độ dài trước khi chạm DB để không tiêu thụ token khi mật khẩu chưa hợp lệ.
  if (newPassword.length < 8) {
    throw new AppError('WEAK_PASSWORD', 400, 'Mật khẩu phải chứa tối thiểu 8 ký tự.');
  }

  const tokenHash = hashToken(token);
  const now = new Date();

  // Mọi bước đổi mật khẩu, tiêu thụ token và xóa phiên nằm trong một transaction để không có trạng thái nửa vời.
  return db.$transaction(async (tx) => {
    const record = await tx.emailToken.findUnique({
      where: { tokenHash },
      include: { user: true },
    });

    // Nhận cả 'reset' và 'invite': lời mời nhân viên dùng chung trang đặt mật khẩu; token 'verify' thì không được dùng ở đây.
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

    // Tiêu thụ token bằng lệnh có điều kiện usedAt: null để hai request gửi cùng lúc chỉ có một request thành công.
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

    // Băm sau khi đã giữ được token, để request thua cuộc không tốn công băm Argon2.
    const passwordHash = await hashPassword(newPassword);

    await tx.user.update({
      where: { id: record.userId },
      data: {
        passwordHash,
        // Nhận được thư và bấm link chứng tỏ người này sở hữu hòm thư, nên coi như đã xác thực email.
        emailVerified: true,
      },
    });

    // Vô hiệu các link đặt lại hoặc lời mời khác còn hạn của cùng người này, để không còn link cũ nào dùng lại được.
    await tx.emailToken.updateMany({
      where: {
        userId: record.userId,
        id: { not: record.id },
        usedAt: null,
        purpose: { in: ['reset', 'invite'] },
      },
      data: { usedAt: now },
    });

    // Xóa mọi phiên: nếu mật khẩu bị đổi vì nghi bị lộ thì ai đang đăng nhập bằng mật khẩu cũ cũng bị đá ra.
    await tx.session.deleteMany({
      where: { userId: record.userId },
    });

    return {
      userKind: record.user.kind,
    };
  });
}
