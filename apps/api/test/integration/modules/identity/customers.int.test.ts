import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import { hashPassword } from '../../../../src/modules/identity/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/seed';
import { generateToken, hashToken } from '../../../../src/modules/identity/tokens';

describe('ID-14 customer lookup and lock/unlock integration', () => {
  const app = createApp();
  let adminToken: string;
  let adminUserId: string;

  beforeEach(async () => {
    await seedIdentityRoles(db);

    const adminRole = await db.role.findUniqueOrThrow({ where: { code: 'admin' } });
    const user = await db.user.create({
      data: {
        email: 'admin_cust_mgmt@pickory.local',
        passwordHash: await hashPassword('AdminPass123!'),
        kind: 'staff',
        roles: { create: { roleId: adminRole.id } },
      },
    });
    adminUserId = user.id;

    adminToken = generateToken();
    await db.session.create({
      data: {
        tokenHash: hashToken(adminToken),
        userId: user.id,
        expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
      },
    });
  });

  async function createCustomer(email: string, password = 'CustPassword123!') {
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    const passwordHash = await hashPassword(password);
    const user = await db.user.create({
      data: {
        email,
        name: 'Khách hàng A',
        phone: '0912345678',
        passwordHash,
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
      },
    });

    const token = generateToken();
    await db.session.create({
      data: {
        tokenHash: hashToken(token),
        userId: user.id,
        expiresAt: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000),
      },
    });

    return { user, token };
  }

  it('GET /api/admin/customers không trả về passwordHash và hỗ trợ tìm kiếm', async () => {
    await createCustomer('search_target@pickory.local');

    const res = await request(app)
      .get('/api/admin/customers?search=search_target')
      .set('Cookie', `pk_session=${adminToken}`);

    expect(res.status).toBe(200);
    expect(res.body.data.items).toHaveLength(1);
    const found = res.body.data.items[0];
    expect(found.email).toBe('search_target@pickory.local');
    expect(found.passwordHash).toBeUndefined();
    expect(found.password).toBeUndefined();
  });

  it('khách bị khóa thì phiên cũ mất hiệu lực, không đăng nhập lại được, ghi audit log', async () => {
    const { user, token } = await createCustomer('locked_cust@pickory.local', 'Password123!');

    // 1. Khóa khách hàng
    const lockRes = await request(app)
      .post(`/api/admin/customers/${user.id}/lock`)
      .set('Cookie', `pk_session=${adminToken}`)
      .send({});
    expect(lockRes.status).toBe(200);

    // Kiểm tra audit log
    const audits = await db.auditLog.findMany({
      where: {
        action: 'customer.lock',
        targetId: user.id,
      },
    });
    expect(audits).toHaveLength(1);
    expect(audits[0]!.actorId).toBe(adminUserId);

    // 2. Phiên cũ gửi request GET /api/me trở thành guest
    const meRes = await request(app).get('/api/me').set('Cookie', `pk_session=${token}`);
    expect(meRes.body.data.kind).toBe('guest');

    // 3. Khách bị khóa thử đăng nhập lại -> 401
    const loginRes = await request(app).post('/api/auth/login').send({
      email: 'locked_cust@pickory.local',
      password: 'Password123!',
    });
    expect(loginRes.status).toBe(401);

    // 4. Mở khóa khách hàng
    const unlockRes = await request(app)
      .post(`/api/admin/customers/${user.id}/unlock`)
      .set('Cookie', `pk_session=${adminToken}`)
      .send({});
    expect(unlockRes.status).toBe(200);

    // 5. Sau khi mở khóa -> đăng nhập lại thành công
    const loginAgainRes = await request(app).post('/api/auth/login').send({
      email: 'locked_cust@pickory.local',
      password: 'Password123!',
    });
    expect(loginAgainRes.status).toBe(200);
  });
});
