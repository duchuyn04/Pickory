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

const loginSchema = z.object({
  email: z.string().email('Email không đúng định dạng.'),
  password: z
    .string()
    .min(1, 'Mật khẩu không được để trống.')
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

/**
 * POST /api/auth/register
 * Đăng ký tài khoản khách hàng mới, đăng nhập ngay, cấp phiên 30 ngày.
 */
identityRouter.post('/api/auth/register', async (req, res, next) => {
  try {
    const data = registerSchema.parse(req.body);
    const ip = req.ip || req.socket.remoteAddress || '127.0.0.1';
    // Bẫy honeypot: bot điền ô ẩn thì chặn ngay với lỗi 400 chung, không tạo user và không gửi email
    if (
      (typeof data.website === 'string' && data.website.trim().length > 0) ||
      (typeof data.honeypot === 'string' && data.honeypot.trim().length > 0)
    ) {
      throw new AppError('BAD_REQUEST', 400, 'Yêu cầu không hợp lệ.');
    }

    // Giới hạn tần suất đăng ký: tối đa 5 lần / giờ / IP để chống spam và quá tải Argon2
    registerRateLimiter.consume(ip);

    const result = await customerRegister({
      ...data,
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

    res.status(201).json({
      data: {
        user: result.user,
      },
    });
  } catch (err) {
    next(err);
  }
});

/**
 * POST /api/auth/login
 * Đăng nhập khách hàng, tạo phiên 30 ngày.
 */
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

/**
 * GET /api/auth/google/start
 * Khởi tạo luồng OAuth 2.0 PKCE, chuyển hướng sang Google.
 */
identityRouter.get('/api/auth/google/start', (req, res) => {
  const config = loadConfig();
  const { codeVerifier, codeChallenge, state } = generatePkce();

  const returnToRaw = req.query.returnTo as string | undefined;
  const returnTo =
    returnToRaw && returnToRaw.startsWith('/') && !returnToRaw.startsWith('//')
      ? returnToRaw
      : undefined;

  // Lưu state và codeVerifier vào cookie an toàn
  res.cookie('pk_oauth_state', JSON.stringify({ state, codeVerifier, returnTo }), {
    httpOnly: true,
    secure: config.NODE_ENV === 'production',
    sameSite: 'lax',
    maxAge: 10 * 60 * 1000, // 10 phút
  });

  const redirectUri = `${config.API_BASE_URL}/api/auth/google/callback`;
  const clientId = config.GOOGLE_CLIENT_ID;

  const authUrl = buildGoogleAuthUrl({
    clientId,
    redirectUri,
    state,
    codeChallenge,
  });

  if (req.query.json === '1' || req.headers.accept?.includes('application/json')) {
    res.json({ data: { authUrl, state } });
    return;
  }

  res.redirect(authUrl);
});

/**
 * GET /api/auth/google/callback
 * Nhận authorization code từ Google, xác thực state PKCE và đăng nhập khách.
 */
identityRouter.get('/api/auth/google/callback', async (req, res, next) => {
  try {
    const config = loadConfig();
    const code = req.query.code as string | undefined;
    const state = req.query.state as string | undefined;

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
        // ignore
      }
    }

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

    const destination =
      returnTo && returnTo.startsWith('/') && !returnTo.startsWith('//')
        ? `${config.WEB_ORIGIN}${returnTo}`
        : config.WEB_ORIGIN;
    res.redirect(destination);
  } catch (err) {
    next(err);
  }
});

/**
 * POST /api/admin/auth/login
 * Đăng nhập nhân viên (12 giờ, không tự gia hạn, cookie pk_session).
 */
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

/**
 * POST /api/auth/logout
 * Đăng xuất: xóa phiên trong DB và xóa cookie pk_session.
 */
identityRouter.post('/api/auth/logout', async (req, res, next) => {
  try {
    const sessionToken = req.cookies?.pk_session as string | undefined;
    await logout(sessionToken);
    res.clearCookie('pk_session', { httpOnly: true, sameSite: 'lax' });
    res.json({ data: { success: true } });
  } catch (err) {
    next(err);
  }
});

/**
 * POST /api/auth/verify-email
 * Xác thực email bằng token (hạn 24 giờ).
 */
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

/**
 * POST /api/auth/forgot-password
 * Gửi link đặt lại mật khẩu (hạn 30 phút, dùng 1 lần). Luôn trả cùng 1 thông báo.
 */
identityRouter.post('/api/auth/forgot-password', async (req, res, next) => {
  try {
    const { email } = forgotPasswordSchema.parse(req.body);
    const ip = req.ip || req.socket.remoteAddress || '127.0.0.1';
    await forgotPassword(email, ip);
    res.json({
      data: {
        message: 'Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi.',
      },
    });
  } catch (err) {
    next(err);
  }
});

/**
 * POST /api/auth/reset-password
 * Đặt mật khẩu mới bằng token, xóa toàn bộ phiên cũ của người dùng.
 */
identityRouter.post('/api/auth/reset-password', async (req, res, next) => {
  try {
    const { token, newPassword } = resetPasswordSchema.parse(req.body);
    const result = await resetPassword({ token, newPassword });
    res.json({
      data: {
        success: true,
        userKind: result.userKind,
        message: 'Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.',
      },
    });
  } catch (err) {
    next(err);
  }
});

/**
 * GET /api/me
 * Trả thông tin Actor hiện tại (cả khách vãng lai, khách đã đăng nhập và nhân viên).
 */
identityRouter.get('/api/me', async (req, res, next) => {
  try {
    let email: string | null = null;
    let name: string | null = null;
    let phone: string | null = null;

    if (req.actor.userId) {
      const u = await db.user.findUnique({
        where: { id: req.actor.userId },
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
        permissions: Array.from(req.actor.permissions),
        consent: req.actor.consent,
      },
    });
  } catch (err) {
    next(err);
  }
});

const consentInputSchema = z.object({
  necessary: z.literal(true).default(true),
  personalization: z.boolean(),
  marketing: z.boolean().default(false),
});

/**
 * PUT /api/me/consent
 * Lưu lựa chọn đồng ý thu thập dữ liệu (công khai cho cả khách vãng lai và tài khoản).
 */
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
  name: z.string().nullable().optional(),
  phone: z.string().nullable().optional(),
});

/**
 * PATCH /api/me
 * Sửa tên, SĐT của tài khoản hiện tại.
 */
identityRouter.patch('/api/me', requireAuth, async (req, res, next) => {
  try {
    const { name, phone } = updateProfileSchema.parse(req.body);
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
  currentPassword: z.string().optional(),
  newPassword: z
    .string()
    .min(8, 'Mật khẩu mới phải chứa tối thiểu 8 ký tự.')
    .max(128, 'Mật khẩu không được vượt quá 128 ký tự.'),
});

/**
 * POST /api/me/password
 * Đổi mật khẩu, xóa mọi phiên cũ.
 */
identityRouter.post('/api/me/password', requireAuth, async (req, res, next) => {
  try {
    const { currentPassword, newPassword } = changePasswordSchema.parse(req.body);
    await changePassword({
      userId: req.actor.userId!,
      currentPassword,
      newPassword,
    });

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
  password: z.string().optional(),
});

/**
 * DELETE /api/me
 * Xóa tài khoản khách (yêu cầu mật khẩu xác nhận lại).
 */
identityRouter.delete('/api/me', requireAuth, async (req, res, next) => {
  try {
    const { password } = deleteAccountSchema.parse(req.body);
    await deleteCustomerAccount({
      userId: req.actor.userId!,
      password,
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
  startDate: z.string().datetime().optional(),
  endDate: z.string().datetime().optional(),
  page: z.coerce.number().int().min(1).default(1),
  limit: z.coerce.number().int().min(1).max(100).default(20),
});

/**
 * GET /api/admin/audit-logs
 * Tra cứu nhật ký thao tác (chỉ đọc, yêu cầu quyền audit-logs:read).
 */
identityRouter.get(
  '/api/admin/audit-logs',
  requirePermission('audit-logs:read'),
  async (req, res, next) => {
    try {
      const query = auditQuerySchema.parse(req.query);
      const where: Record<string, unknown> = {};

      if (query.actorId) where.actorId = query.actorId;
      if (query.action) where.action = query.action;
      if (query.targetType) where.targetType = query.targetType;

      if (query.startDate || query.endDate) {
        const createdAt: Record<string, Date> = {};
        if (query.startDate) createdAt.gte = new Date(query.startDate);
        if (query.endDate) createdAt.lte = new Date(query.endDate);
        where.createdAt = createdAt;
      }

      const skip = (query.page - 1) * query.limit;
      const [items, total] = await Promise.all([
        db.auditLog.findMany({
          where,
          orderBy: { createdAt: 'desc' },
          skip,
          take: query.limit,
        }),
        db.auditLog.count({ where }),
      ]);

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

/**
 * GET /api/admin/permissions
 * Lấy danh mục quyền hệ thống.
 */
identityRouter.get('/api/admin/permissions', requirePermission('roles:manage'), (_req, res) => {
  res.json({ data: getPermissionsCatalog() });
});

/**
 * GET /api/admin/roles
 * Lấy danh sách role kèm quyền.
 */
identityRouter.get('/api/admin/roles', requireStaff, async (req, res, next) => {
  try {
    const hasRolesManage = req.actor.permissions.has('roles:manage');
    const hasStaffManage = req.actor.permissions.has('staff:manage');
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
  code: z.string().trim().min(1, 'Mã role không được để trống.'),
  name: z.string().trim().min(1, 'Tên role không được để trống.'),
  permissions: z
    .array(z.string())
    .default([])
    .transform((items) => [...new Set(items)]),
});

/**
 * POST /api/admin/roles
 * Tạo role mới.
 */
identityRouter.post(
  '/api/admin/roles',
  requirePermission('roles:manage'),
  async (req, res, next) => {
    try {
      const { code, name, permissions } = createRoleSchema.parse(req.body);
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
    .transform((items) => (items ? [...new Set(items)] : undefined)),
});

/**
 * PATCH /api/admin/roles/:id
 * Cập nhật role.
 */
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

/**
 * DELETE /api/admin/roles/:id
 * Xóa role.
 */
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

/**
 * GET /api/admin/staff
 * Danh sách nhân viên (quyền staff:read).
 */
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
  roleCodes: z
    .array(z.string())
    .min(1, 'Cần chọn ít nhất một vai trò.')
    .transform((items) => [...new Set(items)]),
});

/**
 * POST /api/admin/staff/invitations
 * Mời nhân viên mới (quyền staff:manage).
 */
identityRouter.post(
  '/api/admin/staff/invitations',
  requirePermission('staff:manage'),
  async (req, res, next) => {
    try {
      const { email, name, roleCodes } = inviteStaffSchema.parse(req.body);
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
  roleCodes: z.array(z.string()).transform((items) => [...new Set(items)]),
});

/**
 * PUT /api/admin/staff/:id/roles
 * Gán role cho nhân viên (quyền staff:manage).
 */
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

/**
 * POST /api/admin/staff/:id/lock
 * Khóa tài khoản nhân viên (quyền staff:manage).
 */
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

/**
 * POST /api/admin/staff/:id/unlock
 * Mở khóa tài khoản nhân viên (quyền staff:manage).
 */
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

/**
 * GET /api/admin/customers
 * Tra cứu danh sách khách hàng (quyền customers:read).
 */
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

/**
 * GET /api/admin/customers/:id
 * Xem chi tiết khách hàng (quyền customers:read).
 */
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

/**
 * POST /api/admin/customers/:id/lock
 * Khóa tài khoản khách hàng (quyền customers:manage).
 */
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

/**
 * POST /api/admin/customers/:id/unlock
 * Mở khóa tài khoản khách hàng (quyền customers:manage).
 */
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
