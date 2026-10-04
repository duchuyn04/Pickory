import type { RequestHandler } from 'express';
import { randomUUID } from 'node:crypto';
import { loadConfig } from '../../shared/config';
import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { mergePermissions, type Permission } from './permissions';
import { hashToken } from './tokens';
import type { ConsentState } from './types';

const MUTATING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

/**
 * Middleware định danh và phân quyền Actor kèm phòng thủ CSRF cho mọi request gửi tới hệ thống.
 */
export const actorMiddleware: RequestHandler = async (req, res, next) => {
  try {
    const config = loadConfig();

    // Kiểm tra Origin hoặc Referer với các phương thức thay đổi dữ liệu để chống tấn công CSRF qua trình duyệt.
    if (MUTATING_METHODS.has(req.method.toUpperCase())) {
      const allowedOrigin = new URL(config.WEB_ORIGIN).origin;
      const originHeader = req.headers.origin;
      let reqOrigin: string | null = null;
      if (typeof originHeader === 'string' && originHeader.trim() !== '') {
        try {
          reqOrigin = new URL(originHeader).origin;
        } catch {
          throw new AppError('FORBIDDEN_ORIGIN', 403, 'Yêu cầu bị từ chối do Origin không hợp lệ.');
        }
      } else if (typeof req.headers.referer === 'string' && req.headers.referer.trim() !== '') {
        try {
          reqOrigin = new URL(req.headers.referer).origin;
        } catch {
          throw new AppError(
            'FORBIDDEN_ORIGIN',
            403,
            'Yêu cầu bị từ chối do Referer không hợp lệ.',
          );
        }
      }

      if (!reqOrigin || reqOrigin !== allowedOrigin) {
        throw new AppError(
          'FORBIDDEN_ORIGIN',
          403,
          'Yêu cầu bị từ chối do thiếu hoặc Origin/Referer không hợp lệ.',
        );
      }
    }

    // Cấp định danh ẩn danh dài hạn (1 năm) để bám vết hành vi giỏ hàng và trạng thái đồng ý của khách chưa đăng nhập.
    let anonymousId = req.cookies?.pk_anon as string | undefined;
    if (!anonymousId || typeof anonymousId !== 'string' || anonymousId.trim() === '') {
      anonymousId = randomUUID();
      res.cookie('pk_anon', anonymousId, {
        httpOnly: true,
        secure: config.NODE_ENV === 'production',
        sameSite: 'lax',
        maxAge: 365 * 24 * 60 * 60 * 1000, // 1 năm
      });
    }

    // Nạp phiên làm việc và toàn bộ quyền hạn tương ứng nếu request đính kèm cookie xác thực.
    const rawSessionToken = req.cookies?.pk_session as string | undefined;
    if (rawSessionToken && typeof rawSessionToken === 'string') {
      // Băm SHA-256 token từ cookie trước khi tra cứu vì cơ sở dữ liệu chỉ lưu giá trị băm nhằm bảo vệ phiên.
      const tokenHash = hashToken(rawSessionToken);
      const session = await db.session.findUnique({
        where: { tokenHash },
        include: {
          user: {
            include: {
              roles: {
                include: {
                  role: {
                    include: {
                      permissions: true,
                    },
                  },
                },
              },
            },
          },
        },
      });

      const now = new Date();
      if (session && session.expiresAt > now && !session.user.lockedAt && !session.user.deletedAt) {
        // Phiên của khách hàng được trượt hạn tự động nếu sắp hết hạn (dưới 7 ngày), phiên nhân viên cố định để bảo mật.
        if (session.user.kind === 'customer') {
          const msRemaining = session.expiresAt.getTime() - now.getTime();
          const sevenDaysMs = 7 * 24 * 60 * 60 * 1000;
          if (msRemaining < sevenDaysMs) {
            const extendedExpiresAt = new Date(now.getTime() + 30 * 24 * 60 * 60 * 1000);
            await db.session.update({
              where: { id: session.id },
              data: { expiresAt: extendedExpiresAt },
            });
            session.expiresAt = extendedExpiresAt;
            res.cookie('pk_session', rawSessionToken, {
              httpOnly: true,
              secure: config.NODE_ENV === 'production',
              sameSite: 'lax',
              maxAge: 30 * 24 * 60 * 60 * 1000,
            });
          }
        }

        const roleCodes = session.user.roles.map((r) => r.role.code);
        const rawPerms = session.user.roles.flatMap((r) =>
          r.role.permissions.map((p) => p.permission),
        );
        const permissions = mergePermissions(roleCodes, rawPerms, false);

        // Lấy bản ghi đồng ý mới nhất theo userId hoặc anonymousId để áp dụng các thiết lập quyền riêng tư hiện hành.
        const latestConsent = await db.consentRecord.findFirst({
          where: {
            OR: [{ userId: session.user.id }, { anonymousId }],
          },
          orderBy: { createdAt: 'desc' },
        });

        const consent: ConsentState = latestConsent
          ? {
              personalization: latestConsent.personalization,
              marketing: latestConsent.marketing,
            }
          : { personalization: false, marketing: false };

        req.actor = {
          anonymousId,
          userId: session.user.id,
          kind: session.user.kind,
          roles: roleCodes,
          permissions,
          consent,
          sessionCreatedAt: session.createdAt,
        };
        next();
        return;
      }

      // Dọn dẹp bản ghi phiên đã hết hạn trong cơ sở dữ liệu và yêu cầu trình duyệt hủy bỏ cookie cũ.
      if (session && session.expiresAt <= now) {
        await db.session.delete({ where: { id: session.id } }).catch(() => {});
      }
      res.clearCookie('pk_session', { httpOnly: true, sameSite: 'lax' });
    }

    // Khi không có phiên đăng nhập hợp lệ, thiết lập Actor ở vai trò khách vãng lai (guest) với quyền tối thiểu.
    const latestGuestConsent = await db.consentRecord.findFirst({
      where: { anonymousId },
      orderBy: { createdAt: 'desc' },
    });

    const consent: ConsentState = latestGuestConsent
      ? {
          personalization: latestGuestConsent.personalization,
          marketing: latestGuestConsent.marketing,
        }
      : { personalization: false, marketing: false };

    req.actor = {
      anonymousId,
      userId: null,
      kind: 'guest',
      roles: [],
      permissions: new Set<Permission>(),
      consent,
    };

    next();
  } catch (err) {
    next(err);
  }
};

/**
 * Middleware kiểm tra yêu cầu đăng nhập bắt buộc đối với khách hàng hoặc nhân viên.
 */
export const requireAuth: RequestHandler = (req, _res, next) => {
  if (!req.actor?.userId) {
    next(new AppError('UNAUTHORIZED', 401, 'Vui lòng đăng nhập để tiếp tục.'));
    return;
  }
  next();
};

/**
 * Middleware chỉ cho phép nhân viên nội bộ truy cập vào endpoint quản trị.
 */
export const requireStaff: RequestHandler = (req, _res, next) => {
  if (!req.actor?.userId) {
    next(new AppError('UNAUTHORIZED', 401, 'Vui lòng đăng nhập để tiếp tục.'));
    return;
  }
  if (req.actor.kind !== 'staff') {
    next(new AppError('FORBIDDEN', 403, 'Bạn không có quyền truy cập khu vực này.'));
    return;
  }
  next();
};

/**
 * Middleware kiểm tra danh sách quyền cụ thể, từ chối nếu Actor thiếu bất kỳ quyền nào.
 */
export function requirePermission(...permissions: Permission[]): RequestHandler {
  return (req, _res, next) => {
    if (!req.actor?.userId) {
      next(new AppError('UNAUTHORIZED', 401, 'Vui lòng đăng nhập để tiếp tục.'));
      return;
    }
    for (const p of permissions) {
      if (!req.actor.permissions.has(p)) {
        next(new AppError('FORBIDDEN', 403, 'Bạn không có đủ quyền thực hiện thao tác này.'));
        return;
      }
    }
    next();
  };
}
