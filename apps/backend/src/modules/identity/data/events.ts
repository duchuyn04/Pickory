import { eventBus } from '../../../shared/events';

// Module khác đăng ký xử lý khi khách vãng lai đăng nhập (ví dụ commerce gộp giỏ hàng).
export function onUserLinked(
  handler: (payload: { anonymousId: string; userId: string }) => void | Promise<void>,
): void {
  // Qua event bus để identity không phải import hay ghi vào bảng của module khác.
  eventBus.on('UserLinkedEvent', handler);
}

// Module khác đăng ký xử lý khi người dùng xóa tài khoản (dọn dữ liệu riêng của họ).
export function onUserDeleted(
  handler: (payload: { userId: string }) => void | Promise<void>,
): void {
  eventBus.on('UserDeletedEvent', handler);
}

// Phát sự kiện sau khi khách đăng ký hoặc đăng nhập thành công.
export async function emitUserLinked(anonymousId: string, userId: string): Promise<void> {
  // await để handler chạy xong; handler lỗi chỉ bị log trong event bus, không làm đăng nhập thất bại.
  await eventBus.emit('UserLinkedEvent', { anonymousId, userId });
}

// Phát sự kiện sau khi xóa tài khoản khách.
export async function emitUserDeleted(userId: string): Promise<void> {
  await eventBus.emit('UserDeletedEvent', { userId });
}
