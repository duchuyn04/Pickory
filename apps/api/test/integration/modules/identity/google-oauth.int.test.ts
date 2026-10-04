import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import { onUserLinked } from '../../../../src/modules/identity/events';
import {
  setGoogleOAuthClient,
  type GoogleOAuthClient,
  type GoogleUserInfo,
} from '../../../../src/modules/identity/google-oauth';
import { hashPassword } from '../../../../src/modules/identity/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/seed';

describe('ID-09 Google OAuth (PKCE & linking) integration', () => {
  const app = createApp();

  let mockUserInfo: GoogleUserInfo = {
    sub: 'google-sub-123',
    email: 'googler@example.com',
    email_verified: true,
    name: 'Google User',
  };

  const mockGoogleClient: GoogleOAuthClient = {
    async exchangeCode() {
      return 'mock_access_token_123';
    },
    async getUserInfo() {
      return mockUserInfo;
    },
  };

  beforeEach(async () => {
    await seedIdentityRoles(db);
    setGoogleOAuthClient(mockGoogleClient);
    mockUserInfo = {
      sub: 'google-sub-123',
      email: 'googler@example.com',
      email_verified: true,
      name: 'Google User',
    };
  });

  it('GET /api/auth/google/start tạo URL có code_challenge PKCE và lưu state', async () => {
    const res = await request(app)
      .get('/api/auth/google/start?json=1')
      .set('Accept', 'application/json');

    expect(res.status).toBe(200);
    expect(res.body.data.authUrl).toContain('code_challenge=');
    expect(res.body.data.authUrl).toContain('code_challenge_method=S256');
    expect(res.body.data.authUrl).toContain('response_type=code');

    const cookies = String(res.headers['set-cookie']);
    expect(cookies).toContain('pk_oauth_state=');
  });

  it('state sai hoặc thiếu bị từ chối 400', async () => {
    const cookieState = JSON.stringify({ state: 'correct-state', codeVerifier: 'verifier123' });

    const res = await request(app)
      .get('/api/auth/google/callback?code=mock_code&state=wrong-state')
      .set('Cookie', `pk_oauth_state=${encodeURIComponent(cookieState)}`);

    expect(res.status).toBe(400);
    expect(res.body.error.code).toBe('INVALID_OAUTH_STATE');
  });

  it('email đã xác thực trùng tài khoản khách thì tự động liên kết', async () => {
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    const existingCustomer = await db.user.create({
      data: {
        email: 'googler@example.com',
        passwordHash: await hashPassword('CustomerPass123!'),
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
      },
    });

    const cookieState = JSON.stringify({ state: 'valid-state', codeVerifier: 'verifier123' });

    const res = await request(app)
      .get('/api/auth/google/callback?code=mock_code&state=valid-state&json=1')
      .set('Accept', 'application/json')
      .set('Cookie', `pk_oauth_state=${encodeURIComponent(cookieState)}`);

    expect(res.status).toBe(200);
    expect(res.body.data.user.id).toBe(existingCustomer.id);

    // Kiểm tra đã tạo bản ghi trong oauth_accounts
    const oauthAccount = await db.oAuthAccount.findUnique({
      where: {
        provider_providerUserId: {
          provider: 'google',
          providerUserId: 'google-sub-123',
        },
      },
    });
    expect(oauthAccount).not.toBeNull();
    expect(oauthAccount!.userId).toBe(existingCustomer.id);
  });

  it('email chưa xác thực trên Google thì không tự liên kết với tài khoản có sẵn', async () => {
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    await db.user.create({
      data: {
        email: 'unverified_google@example.com',
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
      },
    });

    mockUserInfo = {
      sub: 'google-sub-unverified',
      email: 'unverified_google@example.com',
      email_verified: false,
    };

    const cookieState = JSON.stringify({ state: 'valid-state', codeVerifier: 'verifier123' });

    const res = await request(app)
      .get('/api/auth/google/callback?code=mock_code&state=valid-state')
      .set('Cookie', `pk_oauth_state=${encodeURIComponent(cookieState)}`);

    expect(res.status).toBe(400);
    expect(res.body.error.code).toBe('UNVERIFIED_EMAIL');
  });

  it('email thuộc nhân viên thì từ chối đăng nhập Google với mã 403', async () => {
    const adminRole = await db.role.findUniqueOrThrow({ where: { code: 'admin' } });
    await db.user.create({
      data: {
        email: 'staff_google@example.com',
        kind: 'staff',
        roles: { create: { roleId: adminRole.id } },
      },
    });

    mockUserInfo = {
      sub: 'google-sub-staff',
      email: 'staff_google@example.com',
      email_verified: true,
    };

    const cookieState = JSON.stringify({ state: 'valid-state', codeVerifier: 'verifier123' });

    const res = await request(app)
      .get('/api/auth/google/callback?code=mock_code&state=valid-state')
      .set('Cookie', `pk_oauth_state=${encodeURIComponent(cookieState)}`);

    expect(res.status).toBe(403);
  });

  it('phát UserLinkedEvent như đăng nhập thường', async () => {
    const events: { anonymousId: string; userId: string }[] = [];
    onUserLinked((payload) => {
      events.push(payload);
    });

    const cookieState = JSON.stringify({ state: 'valid-state', codeVerifier: 'verifier123' });

    const res = await request(app)
      .get('/api/auth/google/callback?code=mock_code&state=valid-state&json=1')
      .set('Accept', 'application/json')
      .set('Cookie', `pk_oauth_state=${encodeURIComponent(cookieState)}; pk_anon=anon-google-test`);

    expect(res.status).toBe(200);
    expect(events.length).toBeGreaterThan(0);
    expect(events.some((e) => e.anonymousId === 'anon-google-test')).toBe(true);
  });
});
