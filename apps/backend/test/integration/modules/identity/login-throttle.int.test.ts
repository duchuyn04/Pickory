import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import { loginThrottle } from '../../../../src/modules/identity/security/login-throttle';
import { hashPassword } from '../../../../src/modules/identity/security/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/data/seed';

describe('ID-06 login throttle endpoint integration', () => {
  const app = createApp();

  beforeEach(async () => {
    loginThrottle.clear();
    await seedIdentityRoles(db);
  });

  it('lần sai thứ 5 vẫn nhận 401; lần thứ 6 nhận 429 kể cả nhập mật khẩu đúng', async () => {
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    const correctPassword = 'CorrectPassword123!';
    const passwordHash = await hashPassword(correctPassword);

    await db.user.create({
      data: {
        email: 'throttle_test@pickory.local',
        passwordHash,
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
      },
    });

    // 5 lần sai nhận 401
    for (let i = 1; i <= 5; i++) {
      const res = await request(app)
        .post('/api/auth/login')
        .send({ email: 'throttle_test@pickory.local', password: 'WrongPassword999!' });
      expect(res.status).toBe(401);
      expect(res.body.error.code).toBe('INVALID_CREDENTIALS');
    }

    // Lần thứ 6: thử lại với mật khẩu ĐÚNG -> vẫn bị chặn với mã 429
    const res6 = await request(app)
      .post('/api/auth/login')
      .send({ email: 'throttle_test@pickory.local', password: correctPassword });
    expect(res6.status).toBe(429);
    expect(res6.body.error.code).toBe('TOO_MANY_ATTEMPTS');
  });
});
