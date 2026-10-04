import { setupConsentLinkListener } from './services/consent';

// Đăng ký xử lý gộp consent ngay khi module được nạp, nếu không thì sự kiện đăng nhập sẽ không ai nghe.
setupConsentLinkListener();

// Chỉ xuất những gì module khác được phép dùng; các file còn lại là chi tiết nội bộ của identity.
export type { Actor, ConsentState } from './data/types';
export {
  actorMiddleware,
  requireAuth,
  requireStaff,
  requirePermission,
} from './security/middleware';
export { recordAudit } from './data/audit';
export { onUserLinked, onUserDeleted } from './data/events';
export type { Permission } from './data/permissions';
export { PERMISSION_CATALOG, PERMISSIONS } from './data/permissions';
export { identityRouter } from './routes';
export { saveConsent, resolveLatestConsent } from './services/consent';
