import { createHash, randomBytes } from 'node:crypto';
import { loadConfig } from '../../shared/config';
import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { emitUserLinked } from './events';
import { generateToken, hashToken, safeEqual } from './tokens';

export interface GoogleUserInfo {
  sub: string;
  email: string;
  email_verified: boolean;
  name?: string;
  picture?: string;
}

export interface GoogleOAuthClient {
  exchangeCode(code: string, codeVerifier: string, redirectUri: string): Promise<string>;
  getUserInfo(accessToken: string): Promise<GoogleUserInfo>;
}

/**
 * Client mặc định gọi HTTP thật tới Google OAuth 2.0 endpoints.
 */
export const defaultGoogleClient: GoogleOAuthClient = {
  async exchangeCode(code: string, codeVerifier: string, redirectUri: string): Promise<string> {
    const config = loadConfig();
    const clientId = config.GOOGLE_CLIENT_ID;
    const clientSecret = config.GOOGLE_CLIENT_SECRET;
    const params = new URLSearchParams({
      code,
      client_id: clientId,
      client_secret: clientSecret,
      redirect_uri: redirectUri,
      grant_type: 'authorization_code',
      code_verifier: codeVerifier,
    });

    const res = await fetch('https://oauth2.googleapis.com/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: params.toString(),
    });

    if (!res.ok) {
      throw new AppError('GOOGLE_AUTH_FAILED', 400, 'Không thể đổi mã xác thực với Google.');
    }

    const data = (await res.json()) as { access_token?: string };
    if (!data.access_token) {
      throw new AppError('GOOGLE_AUTH_FAILED', 400, 'Phản hồi từ Google thiếu access token.');
    }
    return data.access_token;
  },

  async getUserInfo(accessToken: string): Promise<GoogleUserInfo> {
    const res = await fetch('https://openidconnect.googleapis.com/v1/userinfo', {
      headers: { Authorization: `Bearer ${accessToken}` },
    });

    if (!res.ok) {
      throw new AppError(
        'GOOGLE_AUTH_FAILED',
        400,
        'Không thể lấy thông tin người dùng từ Google.',
      );
    }

    return (await res.json()) as GoogleUserInfo;
  },
};

let activeGoogleClient: GoogleOAuthClient = defaultGoogleClient;

export function setGoogleOAuthClient(client: GoogleOAuthClient): void {
  activeGoogleClient = client;
}

export function generatePkce() {
  const codeVerifier = randomBytes(32).toString('base64url');
  const codeChallenge = createHash('sha256').update(codeVerifier).digest('base64url');
  const state = randomBytes(16).toString('hex');
  return { codeVerifier, codeChallenge, state };
}

export function buildGoogleAuthUrl(params: {
  clientId: string;
  redirectUri: string;
  state: string;
  codeChallenge: string;
}): string {
  const url = new URL('https://accounts.google.com/o/oauth2/v2/auth');
  url.searchParams.set('client_id', params.clientId);
  url.searchParams.set('redirect_uri', params.redirectUri);
  url.searchParams.set('response_type', 'code');
  url.searchParams.set('scope', 'openid email profile');
  url.searchParams.set('state', params.state);
  url.searchParams.set('code_challenge', params.codeChallenge);
  url.searchParams.set('code_challenge_method', 'S256');
  return url.toString();
}

export interface HandleGoogleCallbackParams {
  code: string;
  state: string;
  savedState: string;
  codeVerifier: string;
  redirectUri: string;
  anonymousId: string;
}

export async function handleGoogleCallback(params: HandleGoogleCallbackParams) {
  const { code, state, savedState, codeVerifier, redirectUri, anonymousId } = params;

  // 1. Kiểm tra state bằng constant-time
  if (!state || !savedState || !safeEqual(state, savedState)) {
    throw new AppError(
      'INVALID_OAUTH_STATE',
      400,
      'Mã state không hợp lệ hoặc phiên đăng nhập Google đã hết hạn.',
    );
  }

  // 2. Trao đổi mã lấy access token và user info
  const accessToken = await activeGoogleClient.exchangeCode(code, codeVerifier, redirectUri);
  const userInfo = await activeGoogleClient.getUserInfo(accessToken);

  const normalizedEmail = userInfo.email.trim().toLowerCase();

  // 3. Kiểm tra nếu email thuộc nhân viên -> từ chối
  const existingUser = await db.user.findUnique({
    where: { email: normalizedEmail },
    include: { roles: { include: { role: true } } },
  });

  if (existingUser && existingUser.kind === 'staff') {
    throw new AppError(
      'FORBIDDEN',
      403,
      'Tài khoản nhân viên không được phép đăng nhập bằng Google.',
    );
  }

  // 4. Nếu email chưa được xác thực trên Google và đã có tài khoản sẵn -> không tự liên kết
  if (!userInfo.email_verified && existingUser) {
    throw new AppError(
      'UNVERIFIED_EMAIL',
      400,
      'Email Google chưa được xác thực, không thể tự động liên kết với tài khoản hiện có.',
    );
  }

  // 5. Kiểm tra nếu tài khoản hiện có đã bị khóa hoặc xóa
  if (existingUser && (existingUser.lockedAt || existingUser.deletedAt)) {
    throw new AppError('UNAUTHORIZED', 401, 'Tài khoản không tồn tại hoặc đã bị khóa.');
  }

  let targetUser = existingUser;

  // Kiểm tra tài khoản OAuth đã liên kết trước đó chưa
  const existingOAuth = await db.oAuthAccount.findUnique({
    where: {
      provider_providerUserId: {
        provider: 'google',
        providerUserId: userInfo.sub,
      },
    },
    include: { user: true },
  });

  if (existingOAuth) {
    if (existingOAuth.user.lockedAt || existingOAuth.user.deletedAt) {
      throw new AppError('UNAUTHORIZED', 401, 'Tài khoản không tồn tại hoặc đã bị khóa.');
    }
    targetUser = existingOAuth.user as typeof existingUser;
  } else if (targetUser) {
    const userToLink = targetUser;
    if (!userToLink.emailVerified) {
      // Chống pre-hijacking (chiếm tài khoản trước):
      // Kẻ xấu có thể đã đăng ký trước bằng email nạn nhân kèm mật khẩu riêng.
      // Khi chủ sở hữu email thật đăng nhập bằng Google (đã xác thực trên Google),
      // trong cùng transaction xóa passwordHash, xóa mọi phiên cũ, vô hiệu hóa
      // mọi token reset/verify còn hạn, đặt emailVerified = true, rồi mới tạo liên kết OAuth.
      const now = new Date();
      await db.$transaction(async (tx) => {
        await tx.user.update({
          where: { id: userToLink.id },
          data: {
            passwordHash: null,
            emailVerified: true,
          },
        });
        await tx.session.deleteMany({
          where: { userId: userToLink.id },
        });
        await tx.emailToken.updateMany({
          where: {
            userId: userToLink.id,
            usedAt: null,
          },
          data: { usedAt: now },
        });
        await tx.oAuthAccount.create({
          data: {
            provider: 'google',
            providerUserId: userInfo.sub,
            userId: userToLink.id,
          },
        });
      });
      targetUser = { ...userToLink, emailVerified: true, passwordHash: null };
    } else {
      // Tài khoản khách đã có sẵn và email Google đã xác thực trùng: giữ nguyên hành vi cũ
      await db.oAuthAccount.create({
        data: {
          provider: 'google',
          providerUserId: userInfo.sub,
          userId: userToLink.id,
        },
      });
    }
  } else {
    // Khách mới hoàn toàn: tạo tài khoản khách
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    targetUser = await db.user.create({
      data: {
        email: normalizedEmail,
        emailVerified: userInfo.email_verified,
        name: userInfo.name || null,
        kind: 'customer',
        roles: { create: { roleId: custRole.id } },
        oauthAccounts: {
          create: {
            provider: 'google',
            providerUserId: userInfo.sub,
          },
        },
      },
      include: { roles: { include: { role: true } } },
    });
  }

  if (!targetUser) {
    throw new AppError('AUTH_ERROR', 500, 'Không thể xác định tài khoản người dùng.');
  }

  if (targetUser.lockedAt || targetUser.deletedAt) {
    throw new AppError('UNAUTHORIZED', 401, 'Tài khoản không tồn tại hoặc đã bị khóa.');
  }
  // 6. Tạo phiên 30 ngày cho khách
  const sessionToken = generateToken();
  const sessionTokenHash = hashToken(sessionToken);
  const sessionExpiresAt = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000);

  await db.session.create({
    data: {
      tokenHash: sessionTokenHash,
      userId: targetUser.id,
      expiresAt: sessionExpiresAt,
    },
  });

  // 7. Phát UserLinkedEvent như đăng nhập thường
  await emitUserLinked(anonymousId, targetUser.id);

  return {
    user: {
      id: targetUser.id,
      email: targetUser.email,
      name: targetUser.name,
      kind: 'customer' as const,
    },
    token: sessionToken,
    expiresAt: sessionExpiresAt,
  };
}
