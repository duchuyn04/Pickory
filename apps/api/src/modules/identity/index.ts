import { setupConsentLinkListener } from './consent';

// Đăng ký các handler nội bộ của identity
setupConsentLinkListener();

export type { Actor, ConsentState } from './types';
export { actorMiddleware, requireAuth, requireStaff, requirePermission } from './middleware';
export { recordAudit } from './audit';
export { onUserLinked, onUserDeleted } from './events';
export type { Permission } from './permissions';
export { PERMISSION_CATALOG, PERMISSIONS } from './permissions';
export { identityRouter } from './routes';
export { saveConsent, resolveLatestConsent } from './consent';
