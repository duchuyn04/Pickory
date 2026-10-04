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
 * Middleware định danh Actor cho mọi request:
 * 1. Chặn request ghi có Origin lạ (chống CSRF).
 * 2. Cấp hoặc giữ cookie `pk_anon` (UUID v4, httpOnly, Secure, SameSite=Lax, 1 năm).
 * 3. Nếu có cookie `pk_session`: nạp phiên, kiểm tra hạn dùng, nạp user + role + permissions bằng 1 truy vấn join.
 * 4. Nếu không có phiên hoặc hết hạn: gán actor kiểu `guest`.
 */
export const actorMiddleware: RequestHandler = async (req, res, next) => {
  try {
    const config = loadConfig();

    // 1. Kiểm tra Origin cho các request ghi
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

    // 2. Xử lý cookie pk_anon
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

    // 3. Xử lý phiên đăng nhập nếu có
    const rawSessionToken = req.cookies?.pk_session as string | undefined;
    if (rawSessionToken && typeof rawSessionToken === 'string') {
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
        // Gia hạn phiên khách khi còn dưới 7 ngày hoạt động (phiên nhân viên không gia hạn)
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

        // Nạp consent mới nhất của user
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

      // Phiên không hợp lệ hoặc đã hết hạn: xóa cookie và dọn session nếu có
      if (session && session.expiresAt <= now) {
        await db.session.delete({ where: { id: session.id } }).catch(() => {});
      }
      res.clearCookie('pk_session', { httpOnly: true, sameSite: 'lax' });
    }

    // 4. Khách vãng lai (guest)
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
 * Yêu cầu người dùng đã đăng nhập (customer hoặc staff).
 */
export const requireAuth: RequestHandler = (req, _res, next) => {
  if (!req.actor?.userId) {
    next(new AppError('UNAUTHORIZED', 401, 'Vui lòng đăng nhập để tiếp tục.'));
    return;
  }
  next();
};

/**
 * Yêu cầu actor là nhân viên (staff).
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
 * Yêu cầu có đầy đủ các quyền được truyền vào.
 * 401 nếu chưa đăng nhập; 403 nếu thiếu bất kỳ quyền nào.
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
