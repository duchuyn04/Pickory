import type { Permission } from './permissions';

/**
 * Trạng thái đồng ý của người dùng về việc thu thập và xử lý dữ liệu cá nhân.
 */
export interface ConsentState {
  personalization: boolean;
  marketing: boolean;
}

/**
 * Chủ thể thực hiện request hiện tại, xác định qua cookie phiên hoặc định danh khách vãng lai.
 */
export interface Actor {
  anonymousId: string;
  userId: string | null;
  kind: 'guest' | 'customer' | 'staff';
  roles: string[];
  permissions: ReadonlySet<Permission>;
  consent: ConsentState;
  sessionCreatedAt?: Date;
}

/* eslint-disable @typescript-eslint/no-namespace */
// Mở rộng kiểu Request của Express để gắn đối tượng Actor đã định danh từ actorMiddleware.
declare global {
  namespace Express {
    interface Request {
      actor: Actor;
    }
  }
}
/* eslint-enable @typescript-eslint/no-namespace */
