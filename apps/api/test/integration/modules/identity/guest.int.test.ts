import request from 'supertest';
import { describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';

describe('ID-02: Actor khách vãng lai, kiểm tra Origin, GET /api/me', () => {
  const app = createApp();

  it('lần đầu truy cập nhận cookie pk_anon UUID v4 với đúng thuộc tính; lần sau giữ nguyên', async () => {
    // Lần đầu: chưa có cookie
    const res1 = await request(app).get('/api/me');
    expect(res1.status).toBe(200);

    const cookies = res1.headers['set-cookie'];
    expect(cookies).toBeDefined();
    const setCookieStr = Array.isArray(cookies) ? cookies.join('; ') : String(cookies);
    expect(setCookieStr).toContain('pk_anon=');
    expect(setCookieStr.toLowerCase()).toContain('httponly');
    expect(setCookieStr.toLowerCase()).toContain('samesite=lax');

    const anonId1 = res1.body.data.anonymousId;
    expect(anonId1).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);

    // Lần hai: gửi kèm cookie pk_anon cũ
    const res2 = await request(app).get('/api/me').set('Cookie', `pk_anon=${anonId1}`);
    expect(res2.status).toBe(200);
    expect(res2.body.data.anonymousId).toBe(anonId1);
  });

  it('GET /api/me của khách trả kind: guest, permissions rỗng, consent mặc định tắt', async () => {
    const res = await request(app).get('/api/me');
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({
      userId: null,
      kind: 'guest',
      roles: [],
      permissions: [],
      consent: {
        personalization: false,
        marketing: false,
      },
    });
  });

  it('POST/PUT/PATCH/DELETE với Origin lạ nhận 403; GET không bị ảnh hưởng', async () => {
    // GET với Origin lạ vẫn được phép
    const getRes = await request(app).get('/api/me').set('Origin', 'https://malicious-site.com');
    expect(getRes.status).toBe(200);

    // POST với Origin lạ nhận 403
    const postRes = await request(app)
      .post('/api/health') // hoặc bất kỳ POST nào
      .set('Origin', 'https://malicious-site.com');
    expect(postRes.status).toBe(403);
    expect(postRes.body.error.code).toBe('FORBIDDEN_ORIGIN');

    // PUT với Origin lạ nhận 403
    const putRes = await request(app)
      .put('/api/health')
      .set('Origin', 'https://malicious-site.com');
    expect(putRes.status).toBe(403);

    // DELETE với Origin lạ nhận 403
    const deleteRes = await request(app)
      .delete('/api/health')
      .set('Origin', 'https://malicious-site.com');
    expect(deleteRes.status).toBe(403);

    // POST với Origin hợp lệ (WEB_ORIGIN http://localhost:5173) không bị chặn bởi Origin check
    const validPostRes = await request(app)
      .post('/api/health')
      .set('Origin', 'http://localhost:5173');
    // Có thể 404 vì POST /api/health không có route, nhưng không phải 403 FORBIDDEN_ORIGIN
    expect(validPostRes.status).not.toBe(403);

    // POST thiếu hoàn toàn cả Origin và Referer -> nhận 403 FORBIDDEN_ORIGIN
    const noOriginRes = await request(app).post('/api/health').set('x-skip-origin', 'true');
    expect(noOriginRes.status).toBe(403);
    expect(noOriginRes.body.error.code).toBe('FORBIDDEN_ORIGIN');

    // POST không có Origin nhưng có Referer hợp lệ (từ WEB_ORIGIN) -> vượt qua kiểm tra CSRF
    const refererRes = await request(app)
      .post('/api/health')
      .set('x-skip-origin', 'true')
      .set('Referer', 'http://localhost:5173/some-page');
    expect(refererRes.status).not.toBe(403);
  });
});
