import { eventBus } from '../../shared/events';

/**
 * Đăng ký lắng nghe sự kiện UserLinkedEvent (liên kết khách vãng lai với tài khoản).
 */
export function onUserLinked(
  handler: (payload: { anonymousId: string; userId: string }) => void | Promise<void>,
): void {
  eventBus.on('UserLinkedEvent', handler);
}

/**
 * Đăng ký lắng nghe sự kiện UserDeletedEvent (tài khoản người dùng bị xóa).
 */
export function onUserDeleted(
  handler: (payload: { userId: string }) => void | Promise<void>,
): void {
  eventBus.on('UserDeletedEvent', handler);
}

/**
 * Phát sự kiện UserLinkedEvent qua event bus trong process.
 */
export async function emitUserLinked(anonymousId: string, userId: string): Promise<void> {
  await eventBus.emit('UserLinkedEvent', { anonymousId, userId });
}

/**
 * Phát sự kiện UserDeletedEvent qua event bus trong process.
 */
export async function emitUserDeleted(userId: string): Promise<void> {
  await eventBus.emit('UserDeletedEvent', { userId });
}
