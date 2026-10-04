import { Router } from 'express';
import { z } from 'zod';
import { loadConfig } from '../../shared/config';
import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { changePassword, deleteCustomerAccount, updateProfile } from './account';
import { customerLogin, customerRegister, logout, staffLogin } from './auth';
import { saveConsent } from './consent';
import { getCustomerById, getCustomers, lockCustomer, unlockCustomer } from './customers';
import { forgotPassword, resetPassword, verifyEmail } from './email-tokens';
import { buildGoogleAuthUrl, generatePkce, handleGoogleCallback } from './google-oauth';
import { requireAuth, requirePermission, requireStaff } from './middleware';
import { createRole, deleteRole, getPermissionsCatalog, getRoles, updateRole } from './roles';
import { assignStaffRoles, getStaffList, inviteStaff, lockStaff, unlockStaff } from './staff';
import { registerRateLimiter } from './rate-limit';

export const identityRouter = Router();

// Mỗi route: validate đầu vào bằng zod, gọi hàm nghiệp vụ, trả kết quả; lỗi chuyển hết sang next() để errorHandler xử lý thống nhất.

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
identityRouter.post('/api/auth/register', async (req, res, next) => {
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
identityRouter.post('/api/auth/login', async (req, res, next) => {
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
identityRouter.get('/api/auth/google/start', (req, res) => {
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
identityRouter.get('/api/auth/google/callback', async (req, res, next) => {
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
identityRouter.post('/api/admin/auth/login', async (req, res, next) => {
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
identityRouter.post('/api/auth/logout', async (req, res, next) => {
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
identityRouter.post('/api/auth/verify-email', async (req, res, next) => {
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
identityRouter.post('/api/auth/forgot-password', async (req, res, next) => {
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
identityRouter.post('/api/auth/reset-password', async (req, res, next) => {
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

// GET /api/me: cho giao diện biết người đang dùng là ai, có quyền gì (công khai, khách vãng lai cũng gọi được).
identityRouter.get('/api/me', async (req, res, next) => {
  try {
    let email: string | null = null;
    let name: string | null = null;
    let phone: string | null = null;

    // Chỉ người đã đăng nhập mới có hồ sơ để đọc; khách vãng lai giữ các giá trị null.
    if (req.actor.userId) {
      const u = await db.user.findUnique({
        where: { id: req.actor.userId },
        // Chỉ chọn ba trường cần hiển thị, không lấy cả đối tượng user.
        select: { email: true, name: true, phone: true },
      });
      if (u) {
        email = u.email;
        name = u.name;
        phone = u.phone;
      }
    }

    res.json({
      data: {
        anonymousId: req.actor.anonymousId,
        userId: req.actor.userId,
        email,
        name,
        phone,
        kind: req.actor.kind,
        roles: req.actor.roles,
        // Set không chuyển thành JSON được nên đổi sang mảng.
        permissions: Array.from(req.actor.permissions),
        consent: req.actor.consent,
      },
    });
  } catch (err) {
    next(err);
  }
});

const consentInputSchema = z.object({
  // Nhóm cần thiết luôn bật, không tắt được; chỉ chấp nhận true.
  necessary: z.literal(true).default(true),
  personalization: z.boolean(),
  // Marketing mặc định tắt nếu client không gửi.
  marketing: z.boolean().default(false),
});

// PUT /api/me/consent: lưu lựa chọn đồng ý (công khai, vì khách vãng lai cũng cần chọn).
identityRouter.put('/api/me/consent', async (req, res, next) => {
  try {
    const { personalization, marketing } = consentInputSchema.parse(req.body);
    const record = await saveConsent({
      anonymousId: req.actor.anonymousId,
      userId: req.actor.userId,
      personalization,
      marketing,
    });

    res.json({
      data: {
        consent: {
          necessary: true,
          personalization: record.personalization,
          marketing: record.marketing,
        },
      },
    });
  } catch (err) {
    next(err);
  }
});

const updateProfileSchema = z.object({
  // Cho phép null để người dùng xóa trống trường; không gửi (undefined) nghĩa là không đổi.
  name: z.string().nullable().optional(),
  phone: z.string().nullable().optional(),
});

// PATCH /api/me: sửa tên và số điện thoại của chính mình.
identityRouter.patch('/api/me', requireAuth, async (req, res, next) => {
  try {
    const { name, phone } = updateProfileSchema.parse(req.body);
    // Dấu ! an toàn vì requireAuth đã chặn người chưa đăng nhập; luôn dùng userId của actor, không nhận id từ client.
    const updated = await updateProfile({
      userId: req.actor.userId!,
      name,
      phone,
    });

    res.json({ data: { user: updated } });
  } catch (err) {
    next(err);
  }
});

const changePasswordSchema = z.object({
  // Tùy chọn vì tài khoản chỉ dùng Google chưa có mật khẩu cũ để nhập.
  currentPassword: z.string().optional(),
  newPassword: z
    .string()
    .min(8, 'Mật khẩu mới phải chứa tối thiểu 8 ký tự.')
    .max(128, 'Mật khẩu không được vượt quá 128 ký tự.'),
});

// POST /api/me/password: đổi mật khẩu.
identityRouter.post('/api/me/password', requireAuth, async (req, res, next) => {
  try {
    const { currentPassword, newPassword } = changePasswordSchema.parse(req.body);
    await changePassword({
      userId: req.actor.userId!,
      currentPassword,
      newPassword,
    });

    // Mọi phiên đã bị xóa trong DB, kể cả phiên hiện tại, nên xóa cookie để trình duyệt về trạng thái đăng xuất.
    res.clearCookie('pk_session', { httpOnly: true, sameSite: 'lax' });
    res.json({
      data: {
        success: true,
        message: 'Đổi mật khẩu thành công. Mọi phiên đăng nhập cũ đã được xóa.',
      },
    });
  } catch (err) {
    next(err);
  }
});

const deleteAccountSchema = z.object({
  // Tùy chọn vì tài khoản chỉ dùng Google xác nhận bằng cách đăng nhập lại, không có mật khẩu.
  password: z.string().optional(),
});

// DELETE /api/me: khách tự xóa tài khoản sau khi xác nhận lại.
identityRouter.delete('/api/me', requireAuth, async (req, res, next) => {
  try {
    const { password } = deleteAccountSchema.parse(req.body);
    await deleteCustomerAccount({
      userId: req.actor.userId!,
      password,
      // Dùng để kiểm tra "vừa đăng nhập lại Google trong 5 phút" với tài khoản không có mật khẩu.
      sessionCreatedAt: req.actor.sessionCreatedAt,
    });
    res.clearCookie('pk_session', { httpOnly: true, sameSite: 'lax' });
    res.json({
      data: {
        success: true,
        message: 'Tài khoản của bạn đã được xóa thành công.',
      },
    });
  } catch (err) {
    next(err);
  }
});

const auditQuerySchema = z.object({
  actorId: z.string().optional(),
  action: z.string().optional(),
  targetType: z.string().optional(),
  // datetime() đòi chuỗi ISO có múi giờ (đuôi Z), nên giao diện phải đổi ngày sang ISO trước khi gửi.
  startDate: z.string().datetime().optional(),
  endDate: z.string().datetime().optional(),
  // coerce vì query string luôn là chuỗi.
  page: z.coerce.number().int().min(1).default(1),
  // Trần 100 dòng mỗi trang để một request không kéo về cả bảng nhật ký.
  limit: z.coerce.number().int().min(1).max(100).default(20),
});

// GET /api/admin/audit-logs: xem nhật ký, chỉ đọc (cố ý không có route sửa hay xóa).
identityRouter.get(
  '/api/admin/audit-logs',
  requirePermission('audit-logs:read'),
  async (req, res, next) => {
    try {
      const query = auditQuerySchema.parse(req.query);
      const where: Record<string, unknown> = {};

      // Chỉ thêm điều kiện lọc cho tham số nào có gửi lên.
      if (query.actorId) where.actorId = query.actorId;
      if (query.action) where.action = query.action;
      if (query.targetType) where.targetType = query.targetType;

      if (query.startDate || query.endDate) {
        const createdAt: Record<string, Date> = {};
        // gte và lte để khoảng thời gian gồm cả hai đầu mút.
        if (query.startDate) createdAt.gte = new Date(query.startDate);
        if (query.endDate) createdAt.lte = new Date(query.endDate);
        where.createdAt = createdAt;
      }

      const skip = (query.page - 1) * query.limit;
      const [items, total] = await Promise.all([
        db.auditLog.findMany({
          where,
          // Mới nhất lên đầu vì người xem thường quan tâm thao tác gần đây.
          orderBy: { createdAt: 'desc' },
          skip,
          take: query.limit,
        }),
        db.auditLog.count({ where }),
      ]);

      // Nhật ký chỉ lưu actorId nên tra thêm email và tên để giao diện hiển thị người thực hiện thay vì chuỗi UUID.
      // Lấy một lần cho cả trang (theo danh sách id không trùng) thay vì mỗi dòng một truy vấn.
      const actorIds = [...new Set(items.map((i) => i.actorId))];
      const actors = await db.user.findMany({
        where: { id: { in: actorIds } },
        select: { id: true, email: true, name: true },
      });
      const actorMap = new Map(actors.map((a) => [a.id, a]));

      const enrichedItems = items.map((item) => {
        const actor = actorMap.get(item.actorId);
        return {
          ...item,
          // null khi không tìm thấy người đó để giao diện hiển thị phương án dự phòng.
          actorEmail: actor?.email ?? null,
          actorName: actor?.name ?? null,
        };
      });

      res.json({
        data: {
          items: enrichedItems,
          total,
          page: query.page,
          limit: query.limit,
        },
      });
    } catch (err) {
      next(err);
    }
  },
);

// GET /api/admin/permissions: danh mục quyền cho bảng tick trên giao diện quản lý role.
identityRouter.get('/api/admin/permissions', requirePermission('roles:manage'), (_req, res) => {
  res.json({ data: getPermissionsCatalog() });
});

// GET /api/admin/roles: danh sách role; cho cả người quản lý nhân viên xem vì họ cần chọn role khi mời hay gán.
identityRouter.get('/api/admin/roles', requireStaff, async (req, res, next) => {
  try {
    const hasRolesManage = req.actor.permissions.has('roles:manage');
    const hasStaffManage = req.actor.permissions.has('staff:manage');
    // Phải có một trong hai quyền; không dùng requirePermission vì nó đòi đủ tất cả quyền liệt kê, ở đây chỉ cần một.
    if (!hasRolesManage && !hasStaffManage) {
      throw new AppError('FORBIDDEN', 403, 'Bạn không có quyền xem danh sách vai trò.');
    }
    const roles = await getRoles();
    res.json({ data: roles });
  } catch (err) {
    next(err);
  }
});

const createRoleSchema = z.object({
  // trim trước min để tên toàn khoảng trắng bị coi là trống.
  code: z.string().trim().min(1, 'Mã role không được để trống.'),
  name: z.string().trim().min(1, 'Tên role không được để trống.'),
  permissions: z
    .array(z.string())
    .default([])
    // Khử trùng ngay ở đây để quyền gửi hai lần không gây lỗi khóa chính trùng ở DB.
    .transform((items) => [...new Set(items)]),
});

// POST /api/admin/roles: tạo role mới.
identityRouter.post(
  '/api/admin/roles',
  requirePermission('roles:manage'),
  async (req, res, next) => {
    try {
      const { code, name, permissions } = createRoleSchema.parse(req.body);
      // Truyền role và quyền của người gọi xuống để chặn họ cấp quyền vượt quá quyền của chính họ.
      const role = await createRole({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        code,
        name,
        permissions,
      });
      res.status(201).json({ data: role });
    } catch (err) {
      next(err);
    }
  },
);

const updateRoleSchema = z.object({
  name: z.string().trim().min(1, 'Tên role không được để trống.').optional(),
  permissions: z
    .array(z.string())
    .optional()
    // Không gửi permissions thì giữ undefined để chỉ đổi tên, không xóa hết quyền.
    .transform((items) => (items ? [...new Set(items)] : undefined)),
});

// PATCH /api/admin/roles/:id: sửa tên hoặc quyền của role.
identityRouter.patch(
  '/api/admin/roles/:id',
  requirePermission('roles:manage'),
  async (req, res, next) => {
    try {
      const roleId = req.params.id as string;
      const { name, permissions } = updateRoleSchema.parse(req.body);
      const role = await updateRole({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        roleId,
        name,
        permissions,
      });
      res.json({ data: role });
    } catch (err) {
      next(err);
    }
  },
);

// DELETE /api/admin/roles/:id: xóa role chưa ai giữ.
identityRouter.delete(
  '/api/admin/roles/:id',
  requirePermission('roles:manage'),
  async (req, res, next) => {
    try {
      const roleId = req.params.id as string;
      await deleteRole(req.actor.userId!, roleId);
      res.json({ data: { success: true } });
    } catch (err) {
      next(err);
    }
  },
);

// GET /api/admin/staff: danh sách nhân viên.
identityRouter.get('/api/admin/staff', requirePermission('staff:read'), async (_req, res, next) => {
  try {
    const staff = await getStaffList();
    res.json({ data: staff });
  } catch (err) {
    next(err);
  }
});

const inviteStaffSchema = z.object({
  email: z.string().email('Email không đúng định dạng.'),
  name: z.string().trim().min(1, 'Họ và tên không được để trống.'),
  // Bắt buộc ít nhất một role, vì nhân viên không có role nào thì không làm được gì.
  roleCodes: z
    .array(z.string())
    .min(1, 'Cần chọn ít nhất một vai trò.')
    .transform((items) => [...new Set(items)]),
});

// POST /api/admin/staff/invitations: mời nhân viên mới.
identityRouter.post(
  '/api/admin/staff/invitations',
  requirePermission('staff:manage'),
  async (req, res, next) => {
    try {
      const { email, name, roleCodes } = inviteStaffSchema.parse(req.body);
      // Truyền role và quyền của người mời để chặn họ mời người vào role mạnh hơn quyền của mình.
      const result = await inviteStaff({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        email,
        name,
        roleCodes,
      });
      res.status(201).json({ data: result });
    } catch (err) {
      next(err);
    }
  },
);

const assignRolesSchema = z.object({
  // Cho phép mảng rỗng, nghĩa là gỡ hết role (khi đó phiên của người đó bị xóa).
  roleCodes: z.array(z.string()).transform((items) => [...new Set(items)]),
});

// PUT /api/admin/staff/:id/roles: đặt lại toàn bộ role của một nhân viên.
identityRouter.put(
  '/api/admin/staff/:id/roles',
  requirePermission('staff:manage'),
  async (req, res, next) => {
    try {
      const targetUserId = req.params.id as string;
      const { roleCodes } = assignRolesSchema.parse(req.body);
      const result = await assignStaffRoles({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        targetUserId,
        roleCodes,
      });
      res.json({ data: result });
    } catch (err) {
      next(err);
    }
  },
);

// POST /api/admin/staff/:id/lock: khóa nhân viên.
identityRouter.post(
  '/api/admin/staff/:id/lock',
  requirePermission('staff:manage'),
  async (req, res, next) => {
    try {
      const targetUserId = req.params.id as string;
      await lockStaff({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        targetUserId,
      });
      res.json({ data: { success: true, message: 'Đã khóa tài khoản nhân viên.' } });
    } catch (err) {
      next(err);
    }
  },
);

// POST /api/admin/staff/:id/unlock: mở khóa nhân viên.
identityRouter.post(
  '/api/admin/staff/:id/unlock',
  requirePermission('staff:manage'),
  async (req, res, next) => {
    try {
      const targetUserId = req.params.id as string;
      await unlockStaff({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        targetUserId,
      });
      res.json({ data: { success: true, message: 'Đã mở khóa tài khoản nhân viên.' } });
    } catch (err) {
      next(err);
    }
  },
);

const customerQuerySchema = z.object({
  search: z.string().optional(),
  page: z.coerce.number().int().min(1).default(1),
  limit: z.coerce.number().int().min(1).max(100).default(20),
});

// GET /api/admin/customers: tra cứu khách, cho cả sales và support xem (customers:read).
identityRouter.get(
  '/api/admin/customers',
  requirePermission('customers:read'),
  async (req, res, next) => {
    try {
      const query = customerQuerySchema.parse(req.query);
      const result = await getCustomers(query);
      res.json({ data: result });
    } catch (err) {
      next(err);
    }
  },
);

// GET /api/admin/customers/:id: xem một khách.
identityRouter.get(
  '/api/admin/customers/:id',
  requirePermission('customers:read'),
  async (req, res, next) => {
    try {
      const customerId = req.params.id as string;
      const customer = await getCustomerById(customerId);
      res.json({ data: customer });
    } catch (err) {
      next(err);
    }
  },
);

// POST /api/admin/customers/:id/lock: khóa khách, cần quyền customers:manage (sales và support chỉ có quyền xem).
identityRouter.post(
  '/api/admin/customers/:id/lock',
  requirePermission('customers:manage'),
  async (req, res, next) => {
    try {
      const customerId = req.params.id as string;
      await lockCustomer({
        actorId: req.actor.userId!,
        customerId,
      });
      res.json({ data: { success: true, message: 'Đã khóa tài khoản khách hàng.' } });
    } catch (err) {
      next(err);
    }
  },
);

// POST /api/admin/customers/:id/unlock: mở khóa khách.
identityRouter.post(
  '/api/admin/customers/:id/unlock',
  requirePermission('customers:manage'),
  async (req, res, next) => {
    try {
      const customerId = req.params.id as string;
      await unlockCustomer({
        actorId: req.actor.userId!,
        customerId,
      });
      res.json({ data: { success: true, message: 'Đã mở khóa tài khoản khách hàng.' } });
    } catch (err) {
      next(err);
    }
  },
);
