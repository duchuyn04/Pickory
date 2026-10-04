import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import { onUserDeleted } from '../../../../src/modules/identity/data/events';
import { hashPassword } from '../../../../src/modules/identity/security/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/data/seed';
import { generateToken, hashToken } from '../../../../src/modules/identity/security/tokens';

describe('ID-10 profile, change password, delete customer account integration', () => {
  const app = createApp();

  beforeEach(async () => {
    await seedIdentityRoles(db);
  });

  async function createCustomerSession(email: string, password = 'Password123!') {
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    const passwordHash = await hashPassword(password);
    const user = await db.user.create({
      data: {
        email,
        name: 'Tên Ban Đầu',
        phone: '0901234567',
        passwordHash,
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
      },
    });

    const token = generateToken();
    const tokenHash = hashToken(token);
    await db.session.create({
      data: {
        tokenHash,
        userId: user.id,
        expiresAt: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000),
      },
    });

    return { user, token };
  }

  it('PATCH /api/me cập nhật tên và số điện thoại thành công', async () => {
    const { token } = await createCustomerSession('patch_me@pickory.local');

    const res = await request(app).patch('/api/me').set('Cookie', `pk_session=${token}`).send({
      name: 'Tên Mới Đã Sửa',
      phone: '0988888888',
    });

    expect(res.status).toBe(200);
    expect(res.body.data.user.name).toBe('Tên Mới Đã Sửa');
    expect(res.body.data.user.phone).toBe('0988888888');
  });

  it('POST /api/me/password đổi mật khẩu và xóa mọi phiên cũ', async () => {
    const { user, token: sess1 } = await createCustomerSession(
      'change_pw@pickory.local',
      'OldPassword123!',
    );

    // Tạo phiên thứ hai
    const sess2 = generateToken();
    await db.session.create({
      data: {
        tokenHash: hashToken(sess2),
        userId: user.id,
        expiresAt: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000),
      },
    });

    // 1. Sai mật khẩu hiện tại bị 400
    const failRes = await request(app)
      .post('/api/me/password')
      .set('Cookie', `pk_session=${sess1}`)
      .send({
        currentPassword: 'WrongOldPassword!',
        newPassword: 'BrandNewPassword123!',
      });
    expect(failRes.status).toBe(400);

    // 2. Đổi mật khẩu thành công
    const okRes = await request(app)
      .post('/api/me/password')
      .set('Cookie', `pk_session=${sess1}`)
      .send({
        currentPassword: 'OldPassword123!',
        newPassword: 'BrandNewPassword123!',
      });
    expect(okRes.status).toBe(200);

    // 3. Mọi phiên cũ bị xóa khỏi DB
    const remainingSessions = await db.session.count({ where: { userId: user.id } });
    expect(remainingSessions).toBe(0);

    // 4. Phiên cũ truy cập GET /api/me trở thành guest
    const checkOldRes = await request(app).get('/api/me').set('Cookie', `pk_session=${sess1}`);
    expect(checkOldRes.body.data.kind).toBe('guest');

    // 5. Đăng nhập lại được bằng mật khẩu mới
    const loginNewRes = await request(app).post('/api/auth/login').send({
      email: 'change_pw@pickory.local',
      password: 'BrandNewPassword123!',
    });
    expect(loginNewRes.status).toBe(200);
  });

  it('DELETE /api/me: xóa tài khoản khách, ẩn danh dữ liệu, giữ deletedAt và phát UserDeletedEvent', async () => {
    const originalEmail = 'delete_target@pickory.local';
    const { user, token } = await createCustomerSession(originalEmail, 'DeleteMePass123!');

    // Gắn thêm email token và oauth account cho user
    await db.emailToken.create({
      data: {
        tokenHash: hashToken('some_token'),
        userId: user.id,
        purpose: 'verify',
        expiresAt: new Date(Date.now() + 10000),
      },
    });

    await db.oAuthAccount.create({
      data: {
        provider: 'google',
        providerUserId: 'google_user_delete_123',
        userId: user.id,
      },
    });

    const deletedEvents: { userId: string }[] = [];
    onUserDeleted((p) => {
      deletedEvents.push(p);
    });

    // 1. Thiếu hoặc sai mật khẩu xác nhận: bị từ chối 401
    const noPassRes = await request(app)
      .delete('/api/me')
      .set('Cookie', `pk_session=${token}`)
      .send({});
    expect(noPassRes.status).toBe(401);

    const wrongPassRes = await request(app)
      .delete('/api/me')
      .set('Cookie', `pk_session=${token}`)
      .send({ password: 'WrongPassword!' });
    expect(wrongPassRes.status).toBe(401);

    // 2. Xác nhận đúng mật khẩu: xóa thành công
    const delRes = await request(app)
      .delete('/api/me')
      .set('Cookie', `pk_session=${token}`)
      .send({ password: 'DeleteMePass123!' });
    expect(delRes.status).toBe(200);

    // 3. Email gốc không còn trong DB (users, emailTokens, oauthAccounts)
    const findByOldEmail = await db.user.findUnique({
      where: { email: originalEmail },
    });
    expect(findByOldEmail).toBeNull();

    const remainingOAuth = await db.oAuthAccount.findMany({
      where: { userId: user.id },
    });
    expect(remainingOAuth).toHaveLength(0);

    const remainingTokens = await db.emailToken.findMany({
      where: { userId: user.id },
    });
    expect(remainingTokens).toHaveLength(0);

    // 4. Bản ghi user vẫn còn trong DB với deletedAt và email ẩn danh
    const userInDb = await db.user.findUniqueOrThrow({
      where: { id: user.id },
    });
    expect(userInDb.deletedAt).not.toBeNull();
    expect(userInDb.passwordHash).toBeNull();
    expect(userInDb.name).toBe('Người dùng đã xóa');
    expect(userInDb.email).toBe(`deleted-${user.id}@deleted.pickory.local`);

    // 5. Phát đúng 1 sự kiện UserDeletedEvent
    expect(deletedEvents).toHaveLength(1);
    expect(deletedEvents[0]?.userId).toBe(user.id);

    // 6. Không thể đăng nhập lại được
    const loginAgainRes = await request(app).post('/api/auth/login').send({
      email: originalEmail,
      password: 'DeleteMePass123!',
    });
    expect(loginAgainRes.status).toBe(401);
  });

  it('DELETE /api/me: tài khoản Google-only phải có phiên tạo trong vòng 5 phút (re-auth)', async () => {
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    const googleUser = await db.user.create({
      data: {
        email: 'google_only@pickory.local',
        kind: 'customer',
        passwordHash: null,
        roles: { create: { roleId: custRole.id } },
        oauthAccounts: {
          create: {
            provider: 'google',
            providerUserId: 'g_sub_999',
          },
        },
      },
    });

    // 1. Phiên đã tạo cách đây hơn 5 phút (10 phút trước)
    const oldSessionToken = generateToken();
    await db.session.create({
      data: {
        tokenHash: hashToken(oldSessionToken),
        userId: googleUser.id,
        createdAt: new Date(Date.now() - 10 * 60 * 1000),
        expiresAt: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000),
      },
    });

    // Thử xóa tài khoản bằng phiên cũ -> 401 REAUTH_REQUIRED
    const failRes = await request(app)
      .delete('/api/me')
      .set('Cookie', `pk_session=${oldSessionToken}`)
      .send({});
    expect(failRes.status).toBe(401);
    expect(failRes.body.error.code).toBe('REAUTH_REQUIRED');

    // 2. Phiên vừa mới tạo (trong vòng 5 phút, giả lập vừa đăng nhập lại Google)
    const freshSessionToken = generateToken();
    await db.session.create({
      data: {
        tokenHash: hashToken(freshSessionToken),
        userId: googleUser.id,
        createdAt: new Date(),
        expiresAt: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000),
      },
    });

    const okRes = await request(app)
      .delete('/api/me')
      .set('Cookie', `pk_session=${freshSessionToken}`)
      .send({});
    expect(okRes.status).toBe(200);

    const deletedUser = await db.user.findUniqueOrThrow({ where: { id: googleUser.id } });
    expect(deletedUser.deletedAt).not.toBeNull();
  });
});
