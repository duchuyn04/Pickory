import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import { hashPassword } from '../../../../src/modules/identity/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/seed';
import { hashToken } from '../../../../src/modules/identity/tokens';

describe('ID-03 staff auth & sessions integration', () => {
  const app = createApp();

  beforeEach(async () => {
    await seedIdentityRoles(db);
  });

  async function createTestStaff(email: string, roleCode: string) {
    const role = await db.role.findUniqueOrThrow({ where: { code: roleCode } });
    const passwordHash = await hashPassword('StaffPass123!');
    return db.user.create({
      data: {
        email,
        emailVerified: true,
        passwordHash,
        name: 'Nhân viên test',
        kind: 'staff',
        roles: {
          create: {
            roleId: role.id,
          },
        },
      },
    });
  }

  it('sai email và sai mật khẩu cho cùng một thông báo lỗi', async () => {
    await createTestStaff('staff1@pickory.local', 'sales');

    // Sai email
    const res1 = await request(app)
      .post('/api/admin/auth/login')
      .send({ email: 'nonexistent@pickory.local', password: 'AnyPassword123!' });
    expect(res1.status).toBe(401);
    expect(res1.body).toEqual({
      error: {
        code: 'INVALID_CREDENTIALS',
        message: 'Email hoặc mật khẩu không chính xác.',
      },
    });

    // Sai mật khẩu
    const res2 = await request(app)
      .post('/api/admin/auth/login')
      .send({ email: 'staff1@pickory.local', password: 'WrongPassword999!' });
    expect(res2.status).toBe(401);
    expect(res2.body).toEqual({
      error: {
        code: 'INVALID_CREDENTIALS',
        message: 'Email hoặc mật khẩu không chính xác.',
      },
    });
  });

  it('tài khoản customer không thể đăng nhập ở endpoint nhân viên', async () => {
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    const passwordHash = await hashPassword('CustomerPass123!');
    await db.user.create({
      data: {
        email: 'customer@pickory.local',
        passwordHash,
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
      },
    });

    const res = await request(app)
      .post('/api/admin/auth/login')
      .send({ email: 'customer@pickory.local', password: 'CustomerPass123!' });
    expect(res.status).toBe(401);
    expect(res.body.error.code).toBe('INVALID_CREDENTIALS');
  });

  it('đăng nhập nhân viên thành công, đặt cookie pk_session và nạp đúng quyền', async () => {
    await createTestStaff('sales@pickory.local', 'sales');

    const loginRes = await request(app)
      .post('/api/admin/auth/login')
      .send({ email: 'sales@pickory.local', password: 'StaffPass123!' });

    expect(loginRes.status).toBe(200);
    expect(loginRes.body.data.user).toMatchObject({
      email: 'sales@pickory.local',
      kind: 'staff',
    });

    const cookies = loginRes.headers['set-cookie'];
    const cookieHeader = Array.isArray(cookies) ? cookies.join('; ') : String(cookies);
    expect(cookieHeader).toContain('pk_session=');
    expect(cookieHeader.toLowerCase()).toContain('httponly');

    // Trích xuất session token
    const match = cookieHeader.match(/pk_session=([^;]+)/);
    const sessionToken = match?.[1] ?? '';

    // DB không lưu token gốc, chỉ lưu hash SHA-256
    const expectedHash = hashToken(sessionToken);
    const dbSession = await db.session.findUnique({
      where: { tokenHash: expectedHash },
    });
    expect(dbSession).not.toBeNull();
    expect(dbSession!.userId).toBe(loginRes.body.data.user.id);
    // GET /api/me với cookie pk_session
    const meRes = await request(app).get('/api/me').set('Cookie', `pk_session=${sessionToken}`);

    expect(meRes.status).toBe(200);
    expect(meRes.body.data.kind).toBe('staff');
    expect(meRes.body.data.roles).toEqual(['sales']);
    expect(meRes.body.data.permissions).toContain('orders:read');
    expect(meRes.body.data.permissions).not.toContain('roles:manage');
  });
  it('đổi quyền của role thì request kế tiếp của nhân viên thấy ngay mà không cần cache', async () => {
    await createTestStaff('sales2@pickory.local', 'sales');

    const loginRes = await request(app)
      .post('/api/admin/auth/login')
      .send({ email: 'sales2@pickory.local', password: 'StaffPass123!' });

    const cookieHeader = String(loginRes.headers['set-cookie']);
    const sessionToken = cookieHeader.match(/pk_session=([^;]+)/)?.[1] ?? '';

    // Trước khi đổi: sales không có quyền 'staff:read'
    const meBefore = await request(app).get('/api/me').set('Cookie', `pk_session=${sessionToken}`);
    expect(meBefore.body.data.permissions).not.toContain('staff:read');

    // Thêm quyền 'staff:read' vào role sales trong DB
    const salesRole = await db.role.findUniqueOrThrow({ where: { code: 'sales' } });
    await db.rolePermission.create({
      data: {
        roleId: salesRole.id,
        permission: 'staff:read',
      },
    });

    // Request kế tiếp: ngay lập tức thấy quyền mới
    const meAfter = await request(app).get('/api/me').set('Cookie', `pk_session=${sessionToken}`);
    expect(meAfter.body.data.permissions).toContain('staff:read');
  });

  it('đăng xuất xóa phiên trong DB và xóa cookie', async () => {
    await createTestStaff('sales3@pickory.local', 'sales');

    const loginRes = await request(app)
      .post('/api/admin/auth/login')
      .send({ email: 'sales3@pickory.local', password: 'StaffPass123!' });

    const cookieHeader = String(loginRes.headers['set-cookie']);
    const sessionToken = cookieHeader.match(/pk_session=([^;]+)/)?.[1] ?? '';

    // Gọi đăng xuất
    const logoutRes = await request(app)
      .post('/api/auth/logout')
      .set('Cookie', `pk_session=${sessionToken}`);

    expect(logoutRes.status).toBe(200);
    expect(logoutRes.body.data.success).toBe(true);

    // Phiên trong DB đã bị xóa
    const dbSession = await db.session.findUnique({
      where: { tokenHash: hashToken(sessionToken) },
    });
    expect(dbSession).toBeNull();

    // Request tiếp theo gửi cookie cũ trở thành guest
    const meRes = await request(app).get('/api/me').set('Cookie', `pk_session=${sessionToken}`);
    expect(meRes.body.data.kind).toBe('guest');
  });

  it('phiên hết hạn thì bị từ chối và trở về guest', async () => {
    const user = await createTestStaff('sales4@pickory.local', 'sales');
    const expiredToken = 'expiredtoken12345678901234567890123456789012';
    const expiredTokenHash = hashToken(expiredToken);

    // Tạo phiên đã quá hạn
    await db.session.create({
      data: {
        tokenHash: expiredTokenHash,
        userId: user.id,
        expiresAt: new Date(Date.now() - 1000), // đã quá hạn 1 giây
      },
    });

    const res = await request(app).get('/api/me').set('Cookie', `pk_session=${expiredToken}`);

    expect(res.status).toBe(200);
    expect(res.body.data.kind).toBe('guest');
  });
});
