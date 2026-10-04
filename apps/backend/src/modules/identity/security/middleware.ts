import type { RequestHandler } from 'express';
import { randomUUID } from 'node:crypto';
import { loadConfig } from '../../../shared/config';
import { db } from '../../../shared/db';
import { AppError } from '../../../shared/errors';
import { mergePermissions, type Permission } from '../data/permissions';
import { hashToken } from './tokens';
import type { ConsentState } from '../data/types';

// Các phương thức làm thay đổi dữ liệu, cần chống CSRF; GET không đổi dữ liệu nên không cần.
const MUTATING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

// Chạy trước mọi route: chống CSRF, cấp cookie ẩn danh, rồi xác định req.actor là khách, khách đã đăng nhập hay nhân viên.
export const actorMiddleware: RequestHandler = async (req, res, next) => {
  try {
    const config = loadConfig();

    if (MUTATING_METHODS.has(req.method.toUpperCase())) {
      // Origin hợp lệ duy nhất là domain của web; lấy phần origin để so sánh không bị lệch dấu gạch chéo cuối.
      const allowedOrigin = new URL(config.WEB_ORIGIN).origin;
      const originHeader = req.headers.origin;
      let reqOrigin: string | null = null;
      if (typeof originHeader === 'string' && originHeader.trim() !== '') {
        try {
          reqOrigin = new URL(originHeader).origin;
        } catch {
          // Origin không phân tích được là dấu hiệu request giả mạo nên từ chối luôn.
          throw new AppError('FORBIDDEN_ORIGIN', 403, 'Yêu cầu bị từ chối do Origin không hợp lệ.');
        }
      } else if (typeof req.headers.referer === 'string' && req.headers.referer.trim() !== '') {
        // Một số trình duyệt không gửi Origin cho một số request ghi, nên dùng Referer làm phương án thay thế.
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

      // Thiếu cả Origin lẫn Referer cũng bị chặn: không chứng minh được request đến từ web của mình.
      if (!reqOrigin || reqOrigin !== allowedOrigin) {
        throw new AppError(
          'FORBIDDEN_ORIGIN',
          403,
          'Yêu cầu bị từ chối do thiếu hoặc Origin/Referer không hợp lệ.',
        );
      }
    }

    let anonymousId = req.cookies?.pk_anon as string | undefined;
    // Chưa có cookie ẩn danh (lần đầu ghé thăm) thì cấp mới, để theo dõi giỏ hàng và lựa chọn đồng ý của khách chưa đăng nhập.
    if (!anonymousId || typeof anonymousId !== 'string' || anonymousId.trim() === '') {
      anonymousId = randomUUID();
      res.cookie('pk_anon', anonymousId, {
        // httpOnly để script trên trang không đọc được cookie.
        httpOnly: true,
        // Chỉ bắt buộc HTTPS ở production vì môi trường dev chạy HTTP.
        secure: config.NODE_ENV === 'production',
        // Lax để cookie không bị gửi kèm trong request ghi từ trang khác.
        sameSite: 'lax',
        maxAge: 365 * 24 * 60 * 60 * 1000, // 1 năm
      });
    }

    const rawSessionToken = req.cookies?.pk_session as string | undefined;
    if (rawSessionToken && typeof rawSessionToken === 'string') {
      // DB chỉ lưu hash của token, nên phải băm token trong cookie rồi mới tra cứu.
      const tokenHash = hashToken(rawSessionToken);
      // Một truy vấn join lấy luôn phiên, user, role và quyền; không cache nên đổi quyền có hiệu lực từ request kế tiếp.
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
      // Phiên chỉ hợp lệ khi chưa hết hạn và người dùng không bị khóa hay xóa; khóa tài khoản vì vậy có hiệu lực ngay.
      if (session && session.expiresAt > now && !session.user.lockedAt && !session.user.deletedAt) {
        // Chỉ phiên khách mới tự gia hạn; phiên nhân viên giữ cố định 12 giờ để giới hạn rủi ro.
        if (session.user.kind === 'customer') {
          const msRemaining = session.expiresAt.getTime() - now.getTime();
          const sevenDaysMs = 7 * 24 * 60 * 60 * 1000;
          // Chỉ gia hạn khi còn dưới 7 ngày, để người dùng đang hoạt động không bị đăng xuất mà không ghi DB ở mỗi request.
          if (msRemaining < sevenDaysMs) {
            const extendedExpiresAt = new Date(now.getTime() + 30 * 24 * 60 * 60 * 1000);
            await db.session.update({
              where: { id: session.id },
              data: { expiresAt: extendedExpiresAt },
            });
            session.expiresAt = extendedExpiresAt;
            // Gửi lại cookie với hạn mới, vì nếu không trình duyệt vẫn xóa cookie theo hạn cũ.
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
        // Hợp quyền của mọi role; truyền false để không ghi cảnh báo quyền lạ ở mỗi request (đã kiểm tra một lần lúc khởi động).
        const permissions = mergePermissions(roleCodes, rawPerms, false);

        // Tìm theo cả userId lẫn anonymousId để lựa chọn đồng ý đưa ra khi chưa đăng nhập vẫn được tính.
        const latestConsent = await db.consentRecord.findFirst({
          where: {
            OR: [{ userId: session.user.id }, { anonymousId }],
          },
          orderBy: { createdAt: 'desc' },
        });

        // Chưa chọn thì mặc định tắt hết.
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
          // Cho phép kiểm tra "vừa đăng nhập" ở các thao tác nhạy cảm như xóa tài khoản.
          sessionCreatedAt: session.createdAt,
        };
        next();
        return;
      }

      // Phiên hết hạn thì xóa khỏi DB cho gọn; .catch để lỗi dọn dẹp không làm hỏng request.
      if (session && session.expiresAt <= now) {
        await db.session.delete({ where: { id: session.id } }).catch(() => {});
      }
      // Cookie không còn dùng được nên xóa để trình duyệt ngừng gửi lên.
      res.clearCookie('pk_session', { httpOnly: true, sameSite: 'lax' });
    }

    // Đến đây là khách vãng lai: không có phiên, hoặc phiên không hợp lệ.
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
      // Khách vãng lai không có quyền back-office nào.
      permissions: new Set<Permission>(),
      consent,
    };

    next();
  } catch (err) {
    next(err);
  }
};

// Chặn người chưa đăng nhập (khách hoặc nhân viên đều qua được).
export const requireAuth: RequestHandler = (req, _res, next) => {
  // 401 vì chưa xác định được danh tính.
  if (!req.actor?.userId) {
    next(new AppError('UNAUTHORIZED', 401, 'Vui lòng đăng nhập để tiếp tục.'));
    return;
  }
  next();
};

// Chặn mọi người không phải nhân viên.
export const requireStaff: RequestHandler = (req, _res, next) => {
  if (!req.actor?.userId) {
    next(new AppError('UNAUTHORIZED', 401, 'Vui lòng đăng nhập để tiếp tục.'));
    return;
  }
  // 403 vì đã biết là ai nhưng người này không được vào khu vực nhân viên.
  if (req.actor.kind !== 'staff') {
    next(new AppError('FORBIDDEN', 403, 'Bạn không có quyền truy cập khu vực này.'));
    return;
  }
  next();
};

// Tạo middleware yêu cầu có đủ tất cả quyền được liệt kê.
export function requirePermission(...permissions: Permission[]): RequestHandler {
  return (req, _res, next) => {
    if (!req.actor?.userId) {
      next(new AppError('UNAUTHORIZED', 401, 'Vui lòng đăng nhập để tiếp tục.'));
      return;
    }
    // Thiếu bất kỳ quyền nào cũng bị từ chối: nhiều quyền nghĩa là cần đủ cả, không phải một trong số đó.
    for (const p of permissions) {
      if (!req.actor.permissions.has(p)) {
        next(new AppError('FORBIDDEN', 403, 'Bạn không có đủ quyền thực hiện thao tác này.'));
        return;
      }
    }
    next();
  };
}
