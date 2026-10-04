import type { ConsentRecord } from '../../generated/prisma/client';
import { db } from '../../shared/db';
import { onUserLinked } from './events';
import type { ConsentState } from './types';

export const CURRENT_POLICY_VERSION = '2026-10-01';

export interface SaveConsentParams {
  anonymousId: string;
  userId?: string | null;
  personalization: boolean;
  marketing: boolean;
  policyVersion?: string;
}

/**
 * Lưu bản ghi đồng ý xử lý dữ liệu của người dùng kèm phiên bản chính sách.
 */
export async function saveConsent(params: SaveConsentParams): Promise<ConsentRecord> {
  const { anonymousId, userId, personalization, marketing, policyVersion } = params;

  // Lưu dưới dạng lịch sử bất biến (append-only) thay vì ghi đè để phục vụ kiểm toán và chứng minh tuân thủ pháp lý.
  return db.consentRecord.create({
    data: {
      anonymousId,
      userId: userId || null,
      personalization,
      marketing,
      policyVersion: policyVersion || CURRENT_POLICY_VERSION,
    },
  });
}

/**
 * Xác định trạng thái đồng ý có hiệu lực bằng cách ưu tiên bản ghi có mốc thời gian mới nhất.
 */
export function resolveLatestConsent(
  guestConsent: { personalization: boolean; marketing: boolean; createdAt: Date } | null,
  userConsent: { personalization: boolean; marketing: boolean; createdAt: Date } | null,
): ConsentState {
  if (!guestConsent && !userConsent) {
    return { personalization: false, marketing: false };
  }
  if (!guestConsent && userConsent) {
    return { personalization: userConsent.personalization, marketing: userConsent.marketing };
  }
  if (guestConsent && !userConsent) {
    return { personalization: guestConsent.personalization, marketing: guestConsent.marketing };
  }

  // Khi cả hai danh tính đều có lựa chọn, quyết định gần nhất của người dùng phản ánh đúng ý nguyện hiện tại.
  if (guestConsent!.createdAt.getTime() >= userConsent!.createdAt.getTime()) {
    return { personalization: guestConsent!.personalization, marketing: guestConsent!.marketing };
  }
  return { personalization: userConsent!.personalization, marketing: userConsent!.marketing };
}

/**
 * Lắng nghe sự kiện liên kết tài khoản để đồng bộ và gộp trạng thái đồng ý giữa khách vãng lai và tài khoản.
 */
export function setupConsentLinkListener(): void {
  onUserLinked(async ({ anonymousId, userId }) => {
    const [latestGuest, latestUser] = await Promise.all([
      db.consentRecord.findFirst({
        where: { anonymousId },
        orderBy: { createdAt: 'desc' },
      }),
      db.consentRecord.findFirst({
        where: { userId },
        orderBy: { createdAt: 'desc' },
      }),
    ]);

    const resolved = resolveLatestConsent(latestGuest, latestUser);

    // Chỉ đồng bộ khi lựa chọn của khách vãng lai mới hơn để tránh ghi đè các cấu hình mới của tài khoản chính.
    if (
      latestGuest &&
      (!latestUser || latestGuest.createdAt.getTime() > latestUser.createdAt.getTime())
    ) {
      await saveConsent({
        anonymousId,
        userId,
        personalization: resolved.personalization,
        marketing: resolved.marketing,
        policyVersion: latestGuest.policyVersion,
      });
    }
  });
}
