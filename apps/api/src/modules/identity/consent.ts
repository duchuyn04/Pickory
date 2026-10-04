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
 * Lưu 1 bản ghi đồng ý mới vào consent_records kèm phiên bản chính sách.
 * Mỗi lần người dùng đổi lựa chọn đều thêm bản ghi mới (không sửa bản ghi cũ).
 */
export async function saveConsent(params: SaveConsentParams): Promise<ConsentRecord> {
  const { anonymousId, userId, personalization, marketing, policyVersion } = params;

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
 * Logic chọn bản ghi đồng ý mới nhất giữa khách vãng lai và tài khoản khi gộp (UserLinkedEvent).
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

  // Cả hai cùng có: lấy bản ghi có thời điểm tạo mới hơn
  if (guestConsent!.createdAt.getTime() >= userConsent!.createdAt.getTime()) {
    return { personalization: guestConsent!.personalization, marketing: guestConsent!.marketing };
  }
  return { personalization: userConsent!.personalization, marketing: userConsent!.marketing };
}

/**
 * Đăng ký xử lý khi UserLinkedEvent phát ra: gộp consent lấy bản ghi mới nhất.
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

    // Nếu guest có bản ghi mới hơn user hoặc user chưa có bản ghi, lưu bản ghi mới cho user
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
