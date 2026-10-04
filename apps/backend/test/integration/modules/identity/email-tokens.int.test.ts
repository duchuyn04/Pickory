import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import {
  InMemoryEmailSender,
  setEmailSender,
} from '../../../../src/modules/identity/data/email-sender';
import { hashPassword } from '../../../../src/modules/identity/security/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/data/seed';
import { generateToken, hashToken } from '../../../../src/modules/identity/security/tokens';

describe('ID-07 email tokens, verify email, forgot and reset password integration', () => {
  const app = createApp();
  let emailSender: InMemoryEmailSender;

  beforeEach(async () => {
    await seedIdentityRoles(db);
    emailSender = new InMemoryEmailSender();
    setEmailSender(emailSender);
  });

  async function createTestCustomer(email: string) {
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    const passwordHash = await hashPassword('InitialPass123!');
    return db.user.create({
      data: {
        email,
        emailVerified: false,
        passwordHash,
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
      },
    });
  }

  it('xác thực email bằng token hợp lệ thành công và không dùng lại được', async () => {
    const user = await createTestCustomer('verify_me@pickory.local');

    // Tạo token verify
    const rawToken = generateToken();
    const tokenHash = hashToken(rawToken);
    await db.emailToken.create({
      data: {
        tokenHash,
        userId: user.id,
        purpose: 'verify',
        expiresAt: new Date(Date.now() + 24 * 60 * 60 * 1000), // hạn 24 giờ
      },
    });

    // Xác thực lần 1: thành công
    const res1 = await request(app).post('/api/auth/verify-email').send({ token: rawToken });
    expect(res1.status).toBe(200);
    expect(res1.body.data.success).toBe(true);

    const updatedUser = await db.user.findUniqueOrThrow({ where: { id: user.id } });
    expect(updatedUser.emailVerified).toBe(true);

    // Xác thực lần 2 bằng cùng token: lỗi 400
    const res2 = await request(app).post('/api/auth/verify-email').send({ token: rawToken });
    expect(res2.status).toBe(400);
    expect(res2.body.error.code).toBe('INVALID_TOKEN');
  });

  it('forgot-password trả cùng response dù email có tồn tại hay không, DB không lưu token gốc', async () => {
    await createTestCustomer('forgot_user@pickory.local');

    // 1. Email tồn tại
    const resExists = await request(app)
      .post('/api/auth/forgot-password')
      .send({ email: 'forgot_user@pickory.local' });
    expect(resExists.status).toBe(200);
    expect(resExists.body.data.message).toContain('hướng dẫn đặt lại mật khẩu đã được gửi');

    // Email được gửi đi
    expect(emailSender.sentMessages).toHaveLength(1);
    const sent = emailSender.sentMessages[0]!;
    expect(sent.to).toBe('forgot_user@pickory.local');
    const rawToken = sent.metadata?.token as string;
    expect(rawToken).toBeDefined();

    // Kiểm tra DB không lưu token gốc
    const foundTokenInDb = await db.emailToken.findFirst({
      where: { tokenHash: rawToken },
    });
    expect(foundTokenInDb).toBeNull();

    // DB lưu hash SHA-256
    const expectedHash = hashToken(rawToken);
    const hashInDb = await db.emailToken.findUnique({
      where: { tokenHash: expectedHash },
    });
    expect(hashInDb).toBeDefined();
    expect(hashInDb?.purpose).toBe('reset');

    // 2. Email không tồn tại: trả cùng response
    const resNotExists = await request(app)
      .post('/api/auth/forgot-password')
      .send({ email: 'nobody_exists@pickory.local' });
    expect(resNotExists.status).toBe(200);
    expect(resNotExists.body.data.message).toEqual(resExists.body.data.message);
  });

  it('đặt lại mật khẩu xóa mọi phiên cũ và đăng nhập được bằng mật khẩu mới', async () => {
    const user = await createTestCustomer('reset_user@pickory.local');

    // Tạo 2 phiên đăng nhập cũ
    const sess1Token = generateToken();
    const sess2Token = generateToken();
    await db.session.createMany({
      data: [
        {
          tokenHash: hashToken(sess1Token),
          userId: user.id,
          expiresAt: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000),
        },
        {
          tokenHash: hashToken(sess2Token),
          userId: user.id,
          expiresAt: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000),
        },
      ],
    });

    // Tạo token reset
    const rawResetToken = generateToken();
    await db.emailToken.create({
      data: {
        tokenHash: hashToken(rawResetToken),
        userId: user.id,
        purpose: 'reset',
        expiresAt: new Date(Date.now() + 30 * 60 * 1000), // hạn 30 phút
      },
    });

    // Đặt lại mật khẩu
    const resetRes = await request(app).post('/api/auth/reset-password').send({
      token: rawResetToken,
      newPassword: 'BrandNewPassword123!',
    });
    expect(resetRes.status).toBe(200);
    expect(resetRes.body.data.success).toBe(true);

    // Mọi phiên cũ bị xóa khỏi DB
    const remainingSessions = await db.session.count({
      where: { userId: user.id },
    });
    expect(remainingSessions).toBe(0);

    // Phiên cũ gửi cookie không còn hiệu lực (trở thành guest)
    const oldSessionRes = await request(app)
      .get('/api/me')
      .set('Cookie', `pk_session=${sess1Token}`);
    expect(oldSessionRes.body.data.kind).toBe('guest');

    // Đăng nhập lại bằng mật khẩu mới thành công
    const newLoginRes = await request(app).post('/api/auth/login').send({
      email: 'reset_user@pickory.local',
      password: 'BrandNewPassword123!',
    });
    expect(newLoginRes.status).toBe(200);

    // Token reset không dùng lại được
    const reuseRes = await request(app).post('/api/auth/reset-password').send({
      token: rawResetToken,
      newPassword: 'AnotherPassword123!',
    });
    expect(reuseRes.status).toBe(400);
    expect(reuseRes.body.error.code).toBe('INVALID_TOKEN');
  });

  it('token hết hạn bị từ chối với mã 400', async () => {
    const user = await createTestCustomer('expired_token_user@pickory.local');
    const rawToken = generateToken();

    // Token đã quá hạn 1 giây
    await db.emailToken.create({
      data: {
        tokenHash: hashToken(rawToken),
        userId: user.id,
        purpose: 'reset',
        expiresAt: new Date(Date.now() - 1000),
      },
    });

    const res = await request(app).post('/api/auth/reset-password').send({
      token: rawToken,
      newPassword: 'NewPassword123!',
    });
    expect(res.status).toBe(400);
    expect(res.body.error.code).toBe('INVALID_TOKEN');
  });

  it('gửi đồng thời hai request với cùng 1 token thì đúng 1 request thành công và 1 request thất bại', async () => {
    const user = await createTestCustomer('concurrent_token_user@pickory.local');
    const rawToken = generateToken();
    await db.emailToken.create({
      data: {
        tokenHash: hashToken(rawToken),
        userId: user.id,
        purpose: 'verify',
        expiresAt: new Date(Date.now() + 60000),
      },
    });

    const [resA, resB] = await Promise.all([
      request(app).post('/api/auth/verify-email').send({ token: rawToken }),
      request(app).post('/api/auth/verify-email').send({ token: rawToken }),
    ]);

    const statuses = [resA.status, resB.status].sort();
    expect(statuses).toEqual([200, 400]);
  });
});
