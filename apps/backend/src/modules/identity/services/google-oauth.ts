import { createHash, randomBytes } from 'node:crypto';
import { loadConfig } from '../../../shared/config';
import { db } from '../../../shared/db';
import { AppError } from '../../../shared/errors';
import { emitUserLinked } from '../data/events';
import { generateToken, hashToken, safeEqual } from '../security/tokens';

export interface GoogleUserInfo {
  // Mã người dùng cố định của Google, không đổi dù người đó đổi email.
  sub: string;
  email: string;
  // Google đã kiểm chứng người này sở hữu email hay chưa; quyết định có được tự liên kết tài khoản hay không.
  email_verified: boolean;
  name?: string;
  picture?: string;
}

// Giao diện gọi Google; tách ra để test thay bằng bản giả, không gọi mạng thật.
export interface GoogleOAuthClient {
  exchangeCode(code: string, codeVerifier: string, redirectUri: string): Promise<string>;
  getUserInfo(accessToken: string): Promise<GoogleUserInfo>;
}

// Bản dùng thật: gọi thẳng các endpoint OAuth của Google.
export const defaultGoogleClient: GoogleOAuthClient = {
  async exchangeCode(code: string, codeVerifier: string, redirectUri: string): Promise<string> {
    const config = loadConfig();
    const clientId = config.GOOGLE_CLIENT_ID;
    const clientSecret = config.GOOGLE_CLIENT_SECRET;
    const params = new URLSearchParams({
      code,
      client_id: clientId,
      client_secret: clientSecret,
      // Phải trùng đúng redirect_uri đã dùng lúc chuyển hướng, nếu không Google từ chối.
      redirect_uri: redirectUri,
      grant_type: 'authorization_code',
      // Google băm lại code_verifier và so với code_challenge gửi lúc đầu; chỉ phía đã tạo ra verifier mới đổi được code.
      code_verifier: codeVerifier,
    });

    const res = await fetch('https://oauth2.googleapis.com/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: params.toString(),
    });

    // Không đưa chi tiết lỗi của Google ra ngoài, chỉ báo chung.
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

// Cho test thay client bằng bản giả để chạy không cần mạng và không gọi Google thật.
export function setGoogleOAuthClient(client: GoogleOAuthClient): void {
  activeGoogleClient = client;
}

// Sinh các giá trị ngẫu nhiên cho một lần đăng nhập Google.
export function generatePkce() {
  // codeVerifier là bí mật chỉ server giữ; dùng base64url vì đúng định dạng PKCE yêu cầu.
  const codeVerifier = randomBytes(32).toString('base64url');
  // Chỉ gửi bản băm (challenge) cho Google; kẻ chặn được redirect không có verifier nên không đổi được code lấy token.
  const codeChallenge = createHash('sha256').update(codeVerifier).digest('base64url');
  // state chống CSRF: callback phải mang đúng giá trị này, chứng minh luồng do chính mình bắt đầu.
  const state = randomBytes(16).toString('hex');
  return { codeVerifier, codeChallenge, state };
}

// Dựng URL chuyển khách sang trang đồng ý của Google.
export function buildGoogleAuthUrl(params: {
  clientId: string;
  redirectUri: string;
  state: string;
  codeChallenge: string;
}): string {
  const url = new URL('https://accounts.google.com/o/oauth2/v2/auth');
  url.searchParams.set('client_id', params.clientId);
  url.searchParams.set('redirect_uri', params.redirectUri);
  // Luồng Authorization Code: Google trả về code, server đổi code lấy token, token không đi qua trình duyệt.
  url.searchParams.set('response_type', 'code');
  // Chỉ xin quyền tối thiểu: định danh, email và tên.
  url.searchParams.set('scope', 'openid email profile');
  url.searchParams.set('state', params.state);
  url.searchParams.set('code_challenge', params.codeChallenge);
  // S256 nghĩa là challenge là SHA-256 của verifier, an toàn hơn kiểu plain.
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

// Xử lý khi Google đưa khách quay về: kiểm tra state, đổi code, rồi tạo hoặc liên kết tài khoản và cấp phiên.
export async function handleGoogleCallback(params: HandleGoogleCallbackParams) {
  const { code, state, savedState, codeVerifier, redirectUri, anonymousId } = params;

  // state trên URL phải khớp state đã lưu trong cookie lúc bắt đầu; so sánh constant-time để không lộ qua thời gian xử lý.
  if (!state || !savedState || !safeEqual(state, savedState)) {
    throw new AppError(
      'INVALID_OAUTH_STATE',
      400,
      'Mã state không hợp lệ hoặc phiên đăng nhập Google đã hết hạn.',
    );
  }

  // Đổi code lấy access token rồi lấy thông tin người dùng; mọi dữ liệu về người dùng lấy từ Google, không tin gì từ phía client.
  const accessToken = await activeGoogleClient.exchangeCode(code, codeVerifier, redirectUri);
  const userInfo = await activeGoogleClient.getUserInfo(accessToken);

  const normalizedEmail = userInfo.email.trim().toLowerCase();

  const existingUser = await db.user.findUnique({
    where: { email: normalizedEmail },
    include: { roles: { include: { role: true } } },
  });

  // Nhân viên chỉ đăng nhập bằng email và mật khẩu theo spec; chặn để không mở thêm đường vào tài khoản có quyền lớn.
  if (existingUser && existingUser.kind === 'staff') {
    throw new AppError(
      'FORBIDDEN',
      403,
      'Tài khoản nhân viên không được phép đăng nhập bằng Google.',
    );
  }

  // Email Google chưa xác thực thì không đủ tin cậy để gộp vào tài khoản có sẵn; ai cũng có thể tạo tài khoản Google với email của người khác.
  if (!userInfo.email_verified && existingUser) {
    throw new AppError(
      'UNVERIFIED_EMAIL',
      400,
      'Email Google chưa được xác thực, không thể tự động liên kết với tài khoản hiện có.',
    );
  }

  // Kiểm tra khóa và xóa ở đây, trước khi tạo bất kỳ liên kết nào, để tài khoản bị khóa không thể liên kết thêm Google.
  if (existingUser && (existingUser.lockedAt || existingUser.deletedAt)) {
    throw new AppError('UNAUTHORIZED', 401, 'Tài khoản không tồn tại hoặc đã bị khóa.');
  }

  let targetUser = existingUser;

  // Ưu tiên tìm theo mã Google (sub) vì nó cố định, trong khi email Google của người đó có thể đã đổi.
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
    // Đã từng liên kết: dùng tài khoản đó, nhưng vẫn chặn nếu nó bị khóa hoặc xóa sau khi liên kết.
    if (existingOAuth.user.lockedAt || existingOAuth.user.deletedAt) {
      throw new AppError('UNAUTHORIZED', 401, 'Tài khoản không tồn tại hoặc đã bị khóa.');
    }
    targetUser = existingOAuth.user as typeof existingUser;
  } else if (targetUser) {
    const userToLink = targetUser;
    if (!userToLink.emailVerified) {
      // Tài khoản này đăng ký bằng email nhưng chưa xác thực: có thể là kẻ xấu đã đăng ký trước bằng email của nạn nhân và đặt mật khẩu của hắn.
      // Google đã xác thực người đang đăng nhập là chủ email thật, nên thu hồi mọi thứ kẻ đăng ký trước có thể còn dùng,
      // làm trong một transaction để không bao giờ có lúc mật khẩu cũ đã bị xóa mà liên kết Google chưa được tạo.
      const now = new Date();
      await db.$transaction(async (tx) => {
        await tx.user.update({
          where: { id: userToLink.id },
          data: {
            // Xóa mật khẩu kẻ đăng ký trước đặt; chủ thật dùng Google, hoặc đặt lại mật khẩu qua email sau.
            passwordHash: null,
            emailVerified: true,
          },
        });
        // Xóa phiên kẻ đó có thể đang giữ.
        await tx.session.deleteMany({
          where: { userId: userToLink.id },
        });
        // Vô hiệu mọi link xác thực hay đặt lại còn hạn mà kẻ đó đã yêu cầu trước đó.
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
      // Cập nhật bản sao trong bộ nhớ cho khớp với DB để các bước sau dùng đúng trạng thái.
      targetUser = { ...userToLink, emailVerified: true, passwordHash: null };
    } else {
      // Email đã xác thực ở cả hai phía trùng nhau nên an toàn để liên kết, giữ nguyên mật khẩu hiện có.
      await db.oAuthAccount.create({
        data: {
          provider: 'google',
          providerUserId: userInfo.sub,
          userId: userToLink.id,
        },
      });
    }
  } else {
    // Chưa có tài khoản nào: tạo khách mới; findUniqueOrThrow vì thiếu role customer là lỗi cấu hình chứ không phải lỗi người dùng.
    const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
    targetUser = await db.user.create({
      data: {
        email: normalizedEmail,
        // Lấy theo Google; không có passwordHash vì tài khoản này chỉ đăng nhập bằng Google.
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

  // Không nên xảy ra, nhưng cần để TypeScript biết targetUser chắc chắn có giá trị ở phía dưới.
  if (!targetUser) {
    throw new AppError('AUTH_ERROR', 500, 'Không thể xác định tài khoản người dùng.');
  }

  // Kiểm tra lần cuối ngay trước khi cấp phiên, phòng nhánh nào đó ở trên bỏ sót.
  if (targetUser.lockedAt || targetUser.deletedAt) {
    throw new AppError('UNAUTHORIZED', 401, 'Tài khoản không tồn tại hoặc đã bị khóa.');
  }

  // Phiên khách 30 ngày, cùng quy tắc với đăng nhập bằng mật khẩu.
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

  // Gộp giỏ hàng khách vãng lai như đăng nhập thường.
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
