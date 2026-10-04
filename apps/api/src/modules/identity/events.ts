import { eventBus } from '../../shared/events';

/**
 * Đăng ký lắng nghe sự kiện liên kết khách vãng lai với tài khoản người dùng chính thức.
 */
export function onUserLinked(
  handler: (payload: { anonymousId: string; userId: string }) => void | Promise<void>,
): void {
  eventBus.on('UserLinkedEvent', handler);
}

/**
 * Đăng ký lắng nghe sự kiện xóa tài khoản người dùng để các module khác dọn dẹp dữ liệu phụ thuộc.
 */
export function onUserDeleted(
  handler: (payload: { userId: string }) => void | Promise<void>,
): void {
  eventBus.on('UserDeletedEvent', handler);
}

/**
 * Phát sự kiện liên kết định danh khách vãng lai với người dùng qua event bus nội bộ.
 */
export async function emitUserLinked(anonymousId: string, userId: string): Promise<void> {
  await eventBus.emit('UserLinkedEvent', { anonymousId, userId });
}

/**
 * Phát sự kiện xóa người dùng qua event bus để thông báo cho toàn bộ hệ thống.
 */
export async function emitUserDeleted(userId: string): Promise<void> {
  await eventBus.emit('UserDeletedEvent', { userId });
}
