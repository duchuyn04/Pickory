import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import { CURRENT_POLICY_VERSION } from '../../../../src/modules/identity/services/consent';
import { hashPassword } from '../../../../src/modules/identity/security/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/data/seed';

describe('ID-08 consent management integration', () => {
  const app = createApp();

  beforeEach(async () => {
    await seedIdentityRoles(db);
  });

  it('mỗi lần đổi lưu 1 bản ghi mới kèm phiên bản chính sách; necessary không tắt được', async () => {
    // 1. Cố ý gửi necessary: false -> 400
    const failRes = await request(app)
      .put('/api/me/consent')
      .set('Cookie', 'pk_anon=anon-consent-1')
      .send({
        necessary: false,
        personalization: true,
      });
    expect(failRes.status).toBe(400);

    // 2. Lần đổi 1: bật personalization
    const res1 = await request(app)
      .put('/api/me/consent')
      .set('Cookie', 'pk_anon=anon-consent-1')
      .send({
        personalization: true,
        marketing: false,
      });
    expect(res1.status).toBe(200);
    expect(res1.body.data.consent).toMatchObject({
      necessary: true,
      personalization: true,
      marketing: false,
    });

    // 3. Lần đổi 2: bật marketing
    const res2 = await request(app)
      .put('/api/me/consent')
      .set('Cookie', 'pk_anon=anon-consent-1')
      .send({
        personalization: true,
        marketing: true,
      });
    expect(res2.status).toBe(200);
    expect(res2.body.data.consent).toMatchObject({
      necessary: true,
      personalization: true,
      marketing: true,
    });

    // Kiểm tra DB có đúng 2 bản ghi cho anon-consent-1 kèm policyVersion
    const records = await db.consentRecord.findMany({
      where: { anonymousId: 'anon-consent-1' },
      orderBy: { createdAt: 'asc' },
    });
    expect(records).toHaveLength(2);
    expect(records[0]?.policyVersion).toBe(CURRENT_POLICY_VERSION);
    expect(records[1]?.policyVersion).toBe(CURRENT_POLICY_VERSION);
  });

  it('khách chưa chọn thì consent.personalization = false, sau khi PUT thì GET /api/me trả đúng', async () => {
    // Trước khi chọn: personalization = false
    const meBefore = await request(app).get('/api/me').set('Cookie', 'pk_anon=anon-consent-fresh');
    expect(meBefore.body.data.consent.personalization).toBe(false);

    // Gửi PUT consent
    await request(app).put('/api/me/consent').set('Cookie', 'pk_anon=anon-consent-fresh').send({
      personalization: true,
      marketing: false,
    });

    // Sau khi chọn: GET /api/me trả personalization = true
    const meAfter = await request(app).get('/api/me').set('Cookie', 'pk_anon=anon-consent-fresh');
    expect(meAfter.body.data.consent.personalization).toBe(true);
  });

  it('sau khi đăng nhập khách, consent áp dụng là bản ghi mới nhất giữa vãng lai và tài khoản', async () => {
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    const passwordHash = await hashPassword('ValidPassword123!');
    const user = await db.user.create({
      data: {
        email: 'consent_merge@pickory.local',
        passwordHash,
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
      },
    });

    // Tài khoản có bản ghi cũ hơn: personalization = false
    await db.consentRecord.create({
      data: {
        anonymousId: 'old-anon-record',
        userId: user.id,
        personalization: false,
        marketing: false,
        policyVersion: CURRENT_POLICY_VERSION,
        createdAt: new Date(Date.now() - 3600 * 1000), // 1 giờ trước
      },
    });

    // Khách vãng lai vừa chọn personalization = true
    await request(app).put('/api/me/consent').set('Cookie', 'pk_anon=new-anon-latest').send({
      personalization: true,
      marketing: true,
    });

    // Đăng nhập khách với cookie pk_anon mới
    const loginRes = await request(app)
      .post('/api/auth/login')
      .set('Cookie', 'pk_anon=new-anon-latest')
      .send({
        email: 'consent_merge@pickory.local',
        password: 'ValidPassword123!',
      });
    expect(loginRes.status).toBe(200);

    const sessionCookie = String(loginRes.headers['set-cookie']).match(/pk_session=([^;]+)/)?.[1];

    // GET /api/me với phiên đăng nhập: lấy bản ghi mới nhất (từ guest vừa gộp)
    const meRes = await request(app).get('/api/me').set('Cookie', `pk_session=${sessionCookie}`);

    expect(meRes.body.data.consent.personalization).toBe(true);
    expect(meRes.body.data.consent.marketing).toBe(true);
  });
});
