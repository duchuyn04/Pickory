import { setupConsentLinkListener } from './consent';

// Đăng ký xử lý gộp consent ngay khi module được nạp, nếu không thì sự kiện đăng nhập sẽ không ai nghe.
setupConsentLinkListener();

// Chỉ xuất những gì module khác được phép dùng; các file còn lại là chi tiết nội bộ của identity.
export type { Actor, ConsentState } from './types';
export { actorMiddleware, requireAuth, requireStaff, requirePermission } from './middleware';
export { recordAudit } from './audit';
export { onUserLinked, onUserDeleted } from './events';
export type { Permission } from './permissions';
export { PERMISSION_CATALOG, PERMISSIONS } from './permissions';
export { identityRouter } from './routes';
export { saveConsent, resolveLatestConsent } from './consent';
