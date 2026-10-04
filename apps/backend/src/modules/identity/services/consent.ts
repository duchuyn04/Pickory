import type { ConsentRecord } from '../../../generated/prisma/client';
import { db } from '../../../shared/db';
import { onUserLinked } from '../data/events';
import type { ConsentState } from '../data/types';

// Đổi giá trị này mỗi khi chính sách quyền riêng tư thay đổi, để biết mỗi lựa chọn được đưa ra theo bản chính sách nào.
export const CURRENT_POLICY_VERSION = '2026-10-01';

export interface SaveConsentParams {
  anonymousId: string;
  userId?: string | null;
  personalization: boolean;
  marketing: boolean;
  policyVersion?: string;
}

// Lưu một lựa chọn đồng ý mới.
export async function saveConsent(params: SaveConsentParams): Promise<ConsentRecord> {
  const { anonymousId, userId, personalization, marketing, policyVersion } = params;

  // Luôn thêm dòng mới, không sửa dòng cũ, để còn lịch sử chứng minh người dùng đã chọn gì và khi nào.
  return db.consentRecord.create({
    data: {
      anonymousId,
      // Khách vãng lai chưa có userId nên lưu null.
      userId: userId || null,
      personalization,
      marketing,
      // Mặc định dùng bản chính sách hiện hành; chỉ truyền riêng khi cần giữ nguyên bản của lựa chọn cũ.
      policyVersion: policyVersion || CURRENT_POLICY_VERSION,
    },
  });
}

// Chọn lựa chọn có hiệu lực giữa phía khách vãng lai và phía tài khoản.
export function resolveLatestConsent(
  guestConsent: { personalization: boolean; marketing: boolean; createdAt: Date } | null,
  userConsent: { personalization: boolean; marketing: boolean; createdAt: Date } | null,
): ConsentState {
  // Chưa ai chọn thì mọi thứ tắt, vì phải có đồng ý chủ động mới được thu thập.
  if (!guestConsent && !userConsent) {
    return { personalization: false, marketing: false };
  }
  // Chỉ một bên từng chọn thì lấy bên đó.
  if (!guestConsent && userConsent) {
    return { personalization: userConsent.personalization, marketing: userConsent.marketing };
  }
  if (guestConsent && !userConsent) {
    return { personalization: guestConsent.personalization, marketing: guestConsent.marketing };
  }

  // Cả hai bên đều từng chọn thì theo lựa chọn mới hơn, vì đó là ý định gần nhất của người dùng.
  // Dấu >= để khi hai bên trùng thời điểm thì ưu tiên phía khách, là lựa chọn vừa đưa ra trên thiết bị đang đăng nhập.
  if (guestConsent!.createdAt.getTime() >= userConsent!.createdAt.getTime()) {
    return { personalization: guestConsent!.personalization, marketing: guestConsent!.marketing };
  }
  return { personalization: userConsent!.personalization, marketing: userConsent!.marketing };
}

// Khi khách đăng nhập, gộp lựa chọn của họ với lựa chọn đã lưu trên tài khoản.
export function setupConsentLinkListener(): void {
  onUserLinked(async ({ anonymousId, userId }) => {
    // Lấy bản ghi mới nhất của cả hai phía cùng lúc cho nhanh.
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

    // Chỉ ghi khi lựa chọn của khách mới hơn: nếu tài khoản đã có lựa chọn mới hơn thì giữ nguyên, không ghi đè.
    if (
      latestGuest &&
      (!latestUser || latestGuest.createdAt.getTime() > latestUser.createdAt.getTime())
    ) {
      await saveConsent({
        anonymousId,
        userId,
        personalization: resolved.personalization,
        marketing: resolved.marketing,
        // Giữ bản chính sách gốc của lựa chọn, không gán thành bản hiện hành vì người dùng chưa đồng ý theo bản đó.
        policyVersion: latestGuest.policyVersion,
      });
    }
  });
}
