import { setupConsentLinkListener } from './consent';

// Kích hoạt lắng nghe sự kiện nội bộ ngay khi khởi động module identity để tự động đồng bộ trạng thái đồng ý dữ liệu.
setupConsentLinkListener();

export type { Actor, ConsentState } from './types';
export { actorMiddleware, requireAuth, requireStaff, requirePermission } from './middleware';
export { recordAudit } from './audit';
export { onUserLinked, onUserDeleted } from './events';
export type { Permission } from './permissions';
export { PERMISSION_CATALOG, PERMISSIONS } from './permissions';
export { identityRouter } from './routes';
export { saveConsent, resolveLatestConsent } from './consent';
