import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import { onUserLinked } from '../../../../src/modules/identity/events';
import { hashPassword } from '../../../../src/modules/identity/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/seed';
import { generateToken, hashToken } from '../../../../src/modules/identity/tokens';

describe('ID-05 customer auth & UserLinkedEvent integration', () => {
  const app = createApp();

  beforeEach(async () => {
    await seedIdentityRoles(db);
  });

  it('đăng ký xong đăng nhập luôn, tạo cookie pk_session và role customer', async () => {
    const res = await request(app).post('/api/auth/register').send({
      email: 'customer1@pickory.local',
      password: 'ValidPassword123!',
      name: 'Khách hàng 1',
    });

    expect(res.status).toBe(201);
    expect(res.body.data.user).toMatchObject({
      email: 'customer1@pickory.local',
      name: 'Khách hàng 1',
      kind: 'customer',
    });

    const cookies = res.headers['set-cookie'];
    const cookieHeader = Array.isArray(cookies) ? cookies.join('; ') : String(cookies);
    expect(cookieHeader).toContain('pk_session=');

    // Mật khẩu dưới 8 ký tự bị 400
    const weakRes = await request(app).post('/api/auth/register').send({
      email: 'weak@pickory.local',
      password: 'short',
    });
    expect(weakRes.status).toBe(400);
  });

  it('email thuộc tài khoản nhân viên không được đăng ký làm khách', async () => {
    const adminRole = await db.role.findUniqueOrThrow({ where: { code: 'admin' } });
    await db.user.create({
      data: {
        email: 'staff_existing@pickory.local',
        passwordHash: await hashPassword('Pass123456!'),
        kind: 'staff',
        roles: { create: { roleId: adminRole.id } },
      },
    });

    const res = await request(app).post('/api/auth/register').send({
      email: 'staff_existing@pickory.local',
      password: 'CustomerPass123!',
    });

    expect(res.status).toBe(400);
    expect(res.body.error.code).toBe('EMAIL_ALREADY_EXISTS');
  });

  it('mỗi lần đăng ký hoặc đăng nhập khách phát đúng 1 UserLinkedEvent; nhân viên không phát', async () => {
    const events: { anonymousId: string; userId: string }[] = [];
    onUserLinked((payload) => {
      events.push(payload);
    });

    // 1. Khách đăng ký phát 1 sự kiện
    const regRes = await request(app)
      .post('/api/auth/register')
      .set('Cookie', 'pk_anon=anon-cust-1')
      .send({
        email: 'cust_event@pickory.local',
        password: 'ValidPassword123!',
      });
    expect(regRes.status).toBe(201);
    expect(events).toHaveLength(1);
    expect(events[0]?.anonymousId).toBe('anon-cust-1');

    // 2. Khách đăng nhập phát tiếp 1 sự kiện
    const loginRes = await request(app)
      .post('/api/auth/login')
      .set('Cookie', 'pk_anon=anon-cust-2')
      .send({
        email: 'cust_event@pickory.local',
        password: 'ValidPassword123!',
      });
    expect(loginRes.status).toBe(200);
    expect(events).toHaveLength(2);
    expect(events[1]?.anonymousId).toBe('anon-cust-2');

    // 3. Nhân viên đăng nhập không phát sự kiện
    const salesRole = await db.role.findUniqueOrThrow({ where: { code: 'sales' } });
    await db.user.create({
      data: {
        email: 'staff_event@pickory.local',
        passwordHash: await hashPassword('ValidPassword123!'),
        kind: 'staff',
        roles: { create: { roleId: salesRole.id } },
      },
    });

    const staffLoginRes = await request(app)
      .post('/api/admin/auth/login')
      .set('Cookie', 'pk_anon=anon-staff-1')
      .send({
        email: 'staff_event@pickory.local',
        password: 'ValidPassword123!',
      });
    expect(staffLoginRes.status).toBe(200);
    // Vẫn chỉ là 2 sự kiện từ khách
    expect(events).toHaveLength(2);
  });

  it('handler sự kiện bị lỗi không làm đăng nhập khách thất bại', async () => {
    onUserLinked(() => {
      throw new Error('Sự cố giả lập trong handler sự kiện UserLinkedEvent');
    });

    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    await db.user.create({
      data: {
        email: 'cust_err@pickory.local',
        passwordHash: await hashPassword('ValidPassword123!'),
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
      },
    });

    const res = await request(app).post('/api/auth/login').send({
      email: 'cust_err@pickory.local',
      password: 'ValidPassword123!',
    });

    expect(res.status).toBe(200);
    expect(res.body.data.user.email).toBe('cust_err@pickory.local');
  });

  it('gia hạn phiên khách khi còn dưới 7 ngày', async () => {
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    const user = await db.user.create({
      data: {
        email: 'sliding@pickory.local',
        passwordHash: await hashPassword('ValidPassword123!'),
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
      },
    });

    const token = generateToken();
    const tokenHash = hashToken(token);
    // Tạo phiên khách còn 3 ngày (dưới ngưỡng 7 ngày)
    const threeDaysRemaining = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000);
    const session = await db.session.create({
      data: {
        tokenHash,
        userId: user.id,
        expiresAt: threeDaysRemaining,
      },
    });

    // Gửi request qua actorMiddleware
    const meRes = await request(app).get('/api/me').set('Cookie', `pk_session=${token}`);

    // Kiểm tra DB phiên đã được gia hạn tới ~30 ngày
    const updatedSession = await db.session.findUniqueOrThrow({
      where: { id: session.id },
    });
    const msDiff = updatedSession.expiresAt.getTime() - Date.now();
    const daysRemaining = msDiff / (24 * 60 * 60 * 1000);
    expect(daysRemaining).toBeGreaterThan(28); // đã được kéo dài lên ~30 ngày

    // Kiểm tra cookie pk_session được gia hạn trong header response trình duyệt
    const setCookies = meRes.headers['set-cookie'];
    const sessionCookieStr = Array.isArray(setCookies)
      ? setCookies.find((c) => c.startsWith('pk_session='))
      : setCookies;
    expect(sessionCookieStr).toBeDefined();
    expect(sessionCookieStr).toContain('Max-Age=2592000');
  });
});
