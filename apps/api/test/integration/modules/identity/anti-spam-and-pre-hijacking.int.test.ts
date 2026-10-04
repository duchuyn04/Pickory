import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { clearAllRateLimits } from '../../../../src/modules/identity/rate-limit';
import {
  setGoogleOAuthClient,
  type GoogleOAuthClient,
  type GoogleUserInfo,
} from '../../../../src/modules/identity/google-oauth';
import { seedIdentityRoles } from '../../../../src/modules/identity/seed';
import { db } from '../../../../src/shared/db';

describe('ID-17 Chống spam và chặn chiếm tài khoản trước (pre-hijacking) integration', () => {
  const app = createApp();

  let mockUserInfo: GoogleUserInfo = {
    sub: 'google-sub-victim',
    email: 'victim@pickory.local',
    email_verified: true,
    name: 'Victim Legitimate User',
  };

  const mockGoogleClient: GoogleOAuthClient = {
    async exchangeCode() {
      return 'mock_access_token_victim';
    },
    async getUserInfo() {
      return mockUserInfo;
    },
  };

  beforeEach(async () => {
    clearAllRateLimits();
    await seedIdentityRoles(db);
    setGoogleOAuthClient(mockGoogleClient);
    mockUserInfo = {
      sub: 'google-sub-victim',
      email: 'victim@pickory.local',
      email_verified: true,
      name: 'Victim Legitimate User',
    };
  });

  describe('Chống spam đăng ký (register rate limit và honeypot)', () => {
    it('đăng ký lần 6 từ cùng một IP nhận 429 và thông báo tiếng Việt, không tạo user', async () => {
      // 5 lần đăng ký đầu tiên thành công
      for (let i = 1; i <= 5; i++) {
        const res = await request(app)
          .post('/api/auth/register')
          .send({
            email: `registered_user_${i}@pickory.local`,
            password: 'ValidPassword123!',
            name: `Người dùng ${i}`,
          });
        expect(res.status).toBe(201);
        expect(res.body.data.user.email).toBe(`registered_user_${i}@pickory.local`);
      }

      // Lần thứ 6 từ cùng IP bị chặn 429
      const sixthRes = await request(app).post('/api/auth/register').send({
        email: 'registered_user_6@pickory.local',
        password: 'ValidPassword123!',
        name: 'Người dùng 6',
      });

      expect(sixthRes.status).toBe(429);
      expect(sixthRes.body.error.code).toBe('TOO_MANY_REQUESTS');
      expect(sixthRes.body.error.message).toContain('Bạn đã đăng ký quá nhiều lần từ địa chỉ này');

      // User thứ 6 không hề được tạo trong database
      const notCreated = await db.user.findUnique({
        where: { email: 'registered_user_6@pickory.local' },
      });
      expect(notCreated).toBeNull();
    });

    it('form honeypot: request có trường website ẩn bị từ chối 400 và không tạo user', async () => {
      const botRes = await request(app).post('/api/auth/register').send({
        email: 'bot_spammer@pickory.local',
        password: 'ValidPassword123!',
        name: 'Spam Bot',
        website: 'https://spam-bot.site',
      });

      expect(botRes.status).toBe(400);
      expect(botRes.body.error.code).toBe('BAD_REQUEST');
      expect(botRes.body.error.message).toContain('Yêu cầu không hợp lệ');

      const botUser = await db.user.findUnique({
        where: { email: 'bot_spammer@pickory.local' },
      });
      expect(botUser).toBeNull();
    });

    it('form honeypot: request có trường honeypot bị từ chối 400 và không tạo user', async () => {
      const botRes = await request(app).post('/api/auth/register').send({
        email: 'bot_honeypot@pickory.local',
        password: 'ValidPassword123!',
        honeypot: 'trapped-bot-value',
      });

      expect(botRes.status).toBe(400);

      const botUser = await db.user.findUnique({
        where: { email: 'bot_honeypot@pickory.local' },
      });
      expect(botUser).toBeNull();
    });

    it('request có website rỗng hoặc không truyền vẫn đăng ký bình thường', async () => {
      const validRes = await request(app).post('/api/auth/register').send({
        email: 'human_user@pickory.local',
        password: 'ValidPassword123!',
        website: '',
      });

      expect(validRes.status).toBe(201);
      expect(validRes.body.data.user.email).toBe('human_user@pickory.local');
    });

    it('với TRUST_PROXY=1, gửi X-Forwarded-For giả mạo nhiều IP khác nhau vẫn bị tính chung 1 IP thật và chặn ở lần 6', async () => {
      // Dựng app với TRUST_PROXY = 1 (tin cậy đúng 1 hop từ reverse proxy phía trước)
      const proxyApp = createApp({ TRUST_PROXY: 1 });
      const realClientIp = '203.0.113.195';

      // Kẻ xấu gửi 5 request với X-Forwarded-For giả mạo IP client khác nhau ở đầu header
      for (let i = 1; i <= 5; i++) {
        const fakeSpoofedIp = `198.51.100.${i}`;
        const res = await request(proxyApp)
          .post('/api/auth/register')
          .set('X-Forwarded-For', `${fakeSpoofedIp}, ${realClientIp}`)
          .send({
            email: `spoofed_user_${i}@pickory.local`,
            password: 'ValidPassword123!',
            name: `User Spoof ${i}`,
          });

        expect(res.status).toBe(201);
      }

      // Lần thứ 6: kẻ xấu đổi sang IP giả mạo thứ 6 nhưng vẫn chung hop thật realClientIp
      const sixthRes = await request(proxyApp)
        .post('/api/auth/register')
        .set('X-Forwarded-For', `198.51.100.6, ${realClientIp}`)
        .send({
          email: 'spoofed_user_6@pickory.local',
          password: 'ValidPassword123!',
          name: 'User Spoof 6',
        });

      // Express chỉ tin 1 hop nên req.ip vẫn là realClientIp, do đó bộ đếm IP phát hiện và chặn 429
      expect(sixthRes.status).toBe(429);
      expect(sixthRes.body.error.code).toBe('TOO_MANY_REQUESTS');

      const user6 = await db.user.findUnique({
        where: { email: 'spoofed_user_6@pickory.local' },
      });
      expect(user6).toBeNull();
    });
  });

  describe('Chống spam quên mật khẩu (forgot-password rate limit)', () => {
    it('forgot-password lần 4 cùng email không tạo token và không gửi email nhưng response giống hệt', async () => {
      // Tạo user có sẵn trong hệ thống
      const regRes = await request(app).post('/api/auth/register').send({
        email: 'victim_forgot@pickory.local',
        password: 'Password123!',
      });
      expect(regRes.status).toBe(201);
      const userId = regRes.body.data.user.id;

      // Xóa các token verify ban đầu để đếm token reset chính xác
      await db.emailToken.deleteMany({ where: { userId } });

      let initialResponseMsg = '';

      // 3 lần đầu thành công bình thường
      for (let i = 1; i <= 3; i++) {
        const res = await request(app)
          .post('/api/auth/forgot-password')
          .send({ email: 'victim_forgot@pickory.local' });

        expect(res.status).toBe(200);
        expect(res.body.data.message).toContain('hướng dẫn đặt lại mật khẩu đã được gửi');
        initialResponseMsg = res.body.data.message;
      }

      // Xác nhận DB đã sinh đúng 3 token reset
      const countAfter3 = await db.emailToken.count({
        where: { userId, purpose: 'reset' },
      });
      expect(countAfter3).toBe(3);

      // Lần thứ 4 cùng email: vượt ngưỡng 3 lần / 15 phút / email
      const fourthRes = await request(app)
        .post('/api/auth/forgot-password')
        .send({ email: 'victim_forgot@pickory.local' });

      // Phản hồi giống hệt lần trước, không báo lỗi và không lộ thông tin
      expect(fourthRes.status).toBe(200);
      expect(fourthRes.body.data.message).toBe(initialResponseMsg);

      // Nhưng trong DB KHÔNG tạo thêm token mới (vẫn là 3)
      const countAfter4 = await db.emailToken.count({
        where: { userId, purpose: 'reset' },
      });
      expect(countAfter4).toBe(3);
    });
  });

  describe('Chống pre-hijacking khi liên kết Google OAuth', () => {
    it('đăng ký bằng mật khẩu, chưa xác thực, rồi Google cùng email: mật khẩu cũ bị vô hiệu và phiên cũ bị xóa', async () => {
      const email = 'victim@pickory.local';
      const attackerPassword = 'AttackerEvilPass123!';

      // Bước 1: Kẻ xấu đăng ký trước bằng email nạn nhân, chưa xác thực email (emailVerified = false)
      const regRes = await request(app).post('/api/auth/register').send({
        email,
        password: attackerPassword,
        name: 'Kẻ xấu đặt tên',
      });
      expect(regRes.status).toBe(201);

      const attackerSessionToken = String(regRes.headers['set-cookie']).match(
        /pk_session=([^;]+)/,
      )?.[1];
      expect(attackerSessionToken).toBeDefined();

      // Xác nhận kẻ xấu đăng nhập được bằng mật khẩu của mình trước khi Google liên kết
      const preLogin = await request(app)
        .post('/api/auth/login')
        .send({ email, password: attackerPassword });
      expect(preLogin.status).toBe(200);

      const userBefore = await db.user.findUniqueOrThrow({
        where: { email },
        include: { sessions: true, emailTokens: true },
      });
      expect(userBefore.emailVerified).toBe(false);
      expect(userBefore.passwordHash).not.toBeNull();
      expect(userBefore.sessions.length).toBeGreaterThanOrEqual(1);

      // Bước 2: Chủ sở hữu thật đăng nhập bằng Google với cùng email (Google đã xác thực)
      const cookieState = JSON.stringify({
        state: 'state-victim-123',
        codeVerifier: 'verifier-123',
      });
      const callbackRes = await request(app)
        .get('/api/auth/google/callback?code=mock_code&state=state-victim-123&json=1')
        .set('Accept', 'application/json')
        .set('Cookie', `pk_oauth_state=${encodeURIComponent(cookieState)}`);

      expect(callbackRes.status).toBe(200);
      expect(callbackRes.body.data.user.email).toBe(email);

      // Bước 3: Kiểm tra các biện pháp an toàn trong DB sau khi liên kết:
      const userAfter = await db.user.findUniqueOrThrow({
        where: { email },
        include: { sessions: true, emailTokens: true, oauthAccounts: true },
      });

      // Email đã được đánh dấu xác thực
      expect(userAfter.emailVerified).toBe(true);
      // Mật khẩu do kẻ xấu đặt đã bị xóa (passwordHash = null)
      expect(userAfter.passwordHash).toBeNull();
      // Tài khoản đã liên kết Google OAuth
      expect(userAfter.oauthAccounts.some((oa) => oa.provider === 'google')).toBe(true);

      // Mọi token xác thực / đặt lại mật khẩu cũ đều đã bị vô hiệu hóa (usedAt !== null)
      for (const token of userAfter.emailTokens) {
        expect(token.usedAt).not.toBeNull();
      }

      // Bước 4: Kẻ xấu không thể dùng mật khẩu cũ để đăng nhập nữa
      const attackerAttempt = await request(app)
        .post('/api/auth/login')
        .send({ email, password: attackerPassword });

      expect(attackerAttempt.status).toBe(401);
      expect(attackerAttempt.body.error.code).toBe('INVALID_CREDENTIALS');

      // Bước 5: Phiên cũ của kẻ xấu đã bị xóa, không thể dùng cookie cũ để truy cập
      const meRes = await request(app)
        .get('/api/me')
        .set('Cookie', `pk_session=${attackerSessionToken}`);

      expect(meRes.status).toBe(200);
      expect(meRes.body.data.kind).toBe('guest'); // Không còn nhận diện là khách đã đăng nhập
    });

    it('tài khoản đã xác thực email từ trước: liên kết Google giữ nguyên mật khẩu cũ và phiên cũ', async () => {
      const email = 'verified_customer@pickory.local';
      const userPassword = 'MySecretPass123!';

      // Đăng ký tài khoản
      const regRes = await request(app).post('/api/auth/register').send({
        email,
        password: userPassword,
        name: 'Khách hàng chuẩn',
      });
      expect(regRes.status).toBe(201);
      const userSessionToken = String(regRes.headers['set-cookie']).match(
        /pk_session=([^;]+)/,
      )?.[1];
      expect(userSessionToken).toBeDefined();

      // Đánh dấu email đã được xác thực hợp lệ
      await db.user.update({
        where: { email },
        data: { emailVerified: true },
      });

      mockUserInfo = {
        sub: 'google-sub-verified',
        email,
        email_verified: true,
        name: 'Google Verified User',
      };

      // Đăng nhập Google
      const cookieState = JSON.stringify({ state: 'state-v-456', codeVerifier: 'verifier-456' });
      const callbackRes = await request(app)
        .get('/api/auth/google/callback?code=mock_code&state=state-v-456&json=1')
        .set('Accept', 'application/json')
        .set('Cookie', `pk_oauth_state=${encodeURIComponent(cookieState)}`);

      expect(callbackRes.status).toBe(200);

      const userAfter = await db.user.findUniqueOrThrow({
        where: { email },
        include: { oauthAccounts: true },
      });

      // Mật khẩu vẫn được giữ nguyên
      expect(userAfter.passwordHash).not.toBeNull();
      expect(userAfter.oauthAccounts.some((oa) => oa.provider === 'google')).toBe(true);

      // Vẫn đăng nhập được bằng mật khẩu cũ bình thường
      const loginRes = await request(app)
        .post('/api/auth/login')
        .send({ email, password: userPassword });
      expect(loginRes.status).toBe(200);

      // Phiên cũ vẫn còn hiệu lực
      const meRes = await request(app)
        .get('/api/me')
        .set('Cookie', `pk_session=${userSessionToken}`);
      expect(meRes.status).toBe(200);
      expect(meRes.body.data.kind).toBe('customer');
    });
  });
});
