import { Router } from 'express';
import { z } from 'zod';
import { loadConfig } from '../../../shared/config';
import { AppError } from '../../../shared/errors';
import { registerRateLimiter } from '../security/rate-limit';
import { customerLogin, customerRegister, logout, staffLogin } from '../services/auth';
import { forgotPassword, resetPassword, verifyEmail } from '../services/email-tokens';
import { buildGoogleAuthUrl, generatePkce, handleGoogleCallback } from '../services/google-oauth';

// Đăng ký, đăng nhập (khách, nhân viên, Google), đăng xuất, xác thực email, quên và đặt lại mật khẩu.
export const authRouter = Router();

const loginSchema = z.object({
  email: z.string().email('Email không đúng định dạng.'),
  password: z
    .string()
    .min(1, 'Mật khẩu không được để trống.')
    // Chặn mật khẩu quá dài, vì băm Argon2 một chuỗi rất dài có thể bị lợi dụng để làm quá tải CPU.
    .max(128, 'Mật khẩu không được vượt quá 128 ký tự.'),
});

const registerSchema = z.object({
  email: z.string().email('Email không đúng định dạng.'),
  password: z
    .string()
    .min(8, 'Mật khẩu phải chứa tối thiểu 8 ký tự.')
    .max(128, 'Mật khẩu không được vượt quá 128 ký tự.'),
  name: z.string().optional(),
  phone: z.string().optional(),
  // Hai ô ẩn bẫy bot: người thật không thấy nên không điền, bot điền mọi ô nên sẽ bị nhận ra.
  website: z.string().optional(),
  honeypot: z.string().optional(),
});

const verifyEmailSchema = z.object({
  token: z.string().min(1, 'Token xác thực không được để trống.'),
});

const forgotPasswordSchema = z.object({
  email: z.string().email('Email không đúng định dạng.'),
});

const resetPasswordSchema = z.object({
  token: z.string().min(1, 'Token không được để trống.'),
  newPassword: z
    .string()
    .min(8, 'Mật khẩu mới phải chứa tối thiểu 8 ký tự.')
    .max(128, 'Mật khẩu không được vượt quá 128 ký tự.'),
});

// POST /api/auth/register: đăng ký khách và đăng nhập luôn.
authRouter.post('/api/auth/register', async (req, res, next) => {
  try {
    const data = registerSchema.parse(req.body);
    // Lấy IP để giới hạn tần suất; 127.0.0.1 chỉ là giá trị dự phòng khi không đọc được IP.
    const ip = req.ip || req.socket.remoteAddress || '127.0.0.1';
    // Bot điền ô ẩn thì từ chối bằng lỗi chung; kiểm tra trước bộ đếm để bot không tiêu hao lượt của IP đó.
    if (
      (typeof data.website === 'string' && data.website.trim().length > 0) ||
      (typeof data.honeypot === 'string' && data.honeypot.trim().length > 0)
    ) {
      throw new AppError('BAD_REQUEST', 400, 'Yêu cầu không hợp lệ.');
    }

    // Tối đa 5 lần đăng ký mỗi giờ mỗi IP; vượt thì ném 429 trước khi tốn CPU băm mật khẩu.
    registerRateLimiter.consume(ip);

    const result = await customerRegister({
      ...data,
      anonymousId: req.actor.anonymousId,
      ip,
    });

    const config = loadConfig();
    res.cookie('pk_session', result.token, {
      // httpOnly để script trên trang không đọc được token phiên, tránh bị đánh cắp qua XSS.
      httpOnly: true,
      // Chỉ bắt buộc HTTPS ở production vì môi trường dev chạy HTTP.
      secure: config.NODE_ENV === 'production',
      // Lax để trình duyệt không gửi cookie kèm request ghi từ trang khác.
      sameSite: 'lax',
      // Cùng 30 ngày với hạn phiên khách trong DB.
      maxAge: 30 * 24 * 60 * 60 * 1000,
    });

    // 201 vì vừa tạo tài khoản mới.
    res.status(201).json({
      data: {
        user: result.user,
      },
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/auth/login: đăng nhập khách.
authRouter.post('/api/auth/login', async (req, res, next) => {
  try {
    const { email, password } = loginSchema.parse(req.body);
    const ip = req.ip || req.socket.remoteAddress || '127.0.0.1';
    const result = await customerLogin({
      email,
      password,
      anonymousId: req.actor.anonymousId,
      ip,
    });

    const config = loadConfig();
    res.cookie('pk_session', result.token, {
      httpOnly: true,
      secure: config.NODE_ENV === 'production',
      sameSite: 'lax',
      maxAge: 30 * 24 * 60 * 60 * 1000,
    });

    res.json({
      data: {
        user: result.user,
      },
    });
  } catch (err) {
    next(err);
  }
});

// GET /api/auth/google/start: bắt đầu đăng nhập Google, chuyển trình duyệt sang Google.
authRouter.get('/api/auth/google/start', (req, res) => {
  const config = loadConfig();
  const { codeVerifier, codeChallenge, state } = generatePkce();

  const returnToRaw = req.query.returnTo as string | undefined;
  // Chỉ nhận đường dẫn nội bộ: bắt đầu bằng "/" nhưng không phải "//" (đó là địa chỉ ngoài kiểu //evil.com), để chặn open redirect.
  const returnTo =
    returnToRaw && returnToRaw.startsWith('/') && !returnToRaw.startsWith('//')
      ? returnToRaw
      : undefined;

  // Giữ state và verifier trong cookie httpOnly để callback đối chiếu; chỉ sống 10 phút, vừa đủ cho một lần đăng nhập.
  res.cookie('pk_oauth_state', JSON.stringify({ state, codeVerifier, returnTo }), {
    httpOnly: true,
    secure: config.NODE_ENV === 'production',
    sameSite: 'lax',
    maxAge: 10 * 60 * 1000, // 10 phút
  });

  // Lấy từ cấu hình thay vì từ header Host của request, vì header đó có thể bị giả mạo.
  const redirectUri = `${config.API_BASE_URL}/api/auth/google/callback`;
  const clientId = config.GOOGLE_CLIENT_ID;

  const authUrl = buildGoogleAuthUrl({
    clientId,
    redirectUri,
    state,
    codeChallenge,
  });

  // Chế độ JSON dành cho test và client muốn tự điều hướng thay vì bị redirect.
  if (req.query.json === '1' || req.headers.accept?.includes('application/json')) {
    res.json({ data: { authUrl, state } });
    return;
  }

  res.redirect(authUrl);
});

// GET /api/auth/google/callback: Google đưa khách quay về, kiểm tra state rồi cấp phiên.
authRouter.get('/api/auth/google/callback', async (req, res, next) => {
  try {
    const config = loadConfig();
    const code = req.query.code as string | undefined;
    const state = req.query.state as string | undefined;

    // Thiếu code hoặc state thì không phải kết quả hợp lệ từ Google (ví dụ người dùng từ chối cấp quyền).
    if (!code || !state) {
      res.status(400).json({
        error: { code: 'INVALID_REQUEST', message: 'Thiếu mã code hoặc state từ Google.' },
      });
      return;
    }

    let savedState = '';
    let codeVerifier = '';
    let returnTo: string | undefined;

    const rawCookie = req.cookies?.pk_oauth_state as string | undefined;
    if (rawCookie) {
      try {
        const parsed = JSON.parse(rawCookie) as {
          state?: string;
          codeVerifier?: string;
          returnTo?: string;
        };
        savedState = parsed.state || '';
        codeVerifier = parsed.codeVerifier || '';
        returnTo = parsed.returnTo;
      } catch {
        // Cookie hỏng thì để savedState rỗng; handleGoogleCallback sẽ từ chối vì state không khớp.
      }
    }

    // Xóa cookie ngay vì state chỉ dùng một lần, tránh bị phát lại.
    res.clearCookie('pk_oauth_state', { httpOnly: true, sameSite: 'lax' });

    const redirectUri = `${config.API_BASE_URL}/api/auth/google/callback`;

    const result = await handleGoogleCallback({
      code,
      state,
      savedState,
      codeVerifier,
      redirectUri,
      anonymousId: req.actor.anonymousId,
    });

    res.cookie('pk_session', result.token, {
      httpOnly: true,
      secure: config.NODE_ENV === 'production',
      sameSite: 'lax',
      maxAge: 30 * 24 * 60 * 60 * 1000,
    });

    if (req.query.json === '1' || req.headers.accept?.includes('application/json')) {
      res.json({ data: { user: result.user } });
      return;
    }

    // Kiểm tra lại returnTo lần nữa dù lúc bắt đầu đã lọc, vì giá trị này đi qua cookie nên không nên tin tuyệt đối.
    const destination =
      returnTo && returnTo.startsWith('/') && !returnTo.startsWith('//')
        ? `${config.WEB_ORIGIN}${returnTo}`
        : config.WEB_ORIGIN;
    res.redirect(destination);
  } catch (err) {
    next(err);
  }
});

// POST /api/admin/auth/login: đăng nhập nhân viên, phiên 12 giờ.
authRouter.post('/api/admin/auth/login', async (req, res, next) => {
  try {
    const { email, password } = loginSchema.parse(req.body);
    const ip = req.ip || req.socket.remoteAddress || '127.0.0.1';
    const result = await staffLogin({ email, password, ip });

    const config = loadConfig();
    res.cookie('pk_session', result.token, {
      httpOnly: true,
      secure: config.NODE_ENV === 'production',
      sameSite: 'lax',
      // 12 giờ, cùng hạn phiên nhân viên trong DB.
      maxAge: 12 * 60 * 60 * 1000,
    });

    res.json({
      data: {
        user: result.user,
      },
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/auth/logout: đăng xuất cho cả khách và nhân viên.
authRouter.post('/api/auth/logout', async (req, res, next) => {
  try {
    const sessionToken = req.cookies?.pk_session as string | undefined;
    // Xóa phiên trong DB trước, để token cũ không dùng lại được dù ai đó còn giữ bản sao cookie.
    await logout(sessionToken);
    res.clearCookie('pk_session', { httpOnly: true, sameSite: 'lax' });
    res.json({ data: { success: true } });
  } catch (err) {
    next(err);
  }
});

// POST /api/auth/verify-email: xác thực email bằng token trong link.
authRouter.post('/api/auth/verify-email', async (req, res, next) => {
  try {
    const { token } = verifyEmailSchema.parse(req.body);
    await verifyEmail(token);
    res.json({
      data: {
        success: true,
        message: 'Xác thực email thành công.',
      },
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/auth/forgot-password: gửi link đặt lại mật khẩu.
authRouter.post('/api/auth/forgot-password', async (req, res, next) => {
  try {
    const { email } = forgotPasswordSchema.parse(req.body);
    const ip = req.ip || req.socket.remoteAddress || '127.0.0.1';
    await forgotPassword(email, ip);
    // Luôn trả cùng một thông báo, dù email có tồn tại hay bị giới hạn tần suất, để không lộ email nào đã đăng ký.
    res.json({
      data: {
        message: 'Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi.',
      },
    });
  } catch (err) {
    next(err);
  }
});

// POST /api/auth/reset-password: đặt mật khẩu mới bằng token; dùng cho cả quên mật khẩu và lời mời nhân viên.
authRouter.post('/api/auth/reset-password', async (req, res, next) => {
  try {
    const { token, newPassword } = resetPasswordSchema.parse(req.body);
    const result = await resetPassword({ token, newPassword });
    res.json({
      data: {
        success: true,
        // Trả loại tài khoản để giao diện đưa nhân viên sang trang đăng nhập nhân viên thay vì trang khách.
        userKind: result.userKind,
        message: 'Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.',
      },
    });
  } catch (err) {
    next(err);
  }
});
