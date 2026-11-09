import type { HandledCallbackStatus } from './support-desk-api';
import type { CallbackRequest } from './support-desk-schemas';

export const DELETED_PHONE_LABEL = 'Đã xóa số';

export const STATUS_LABELS: Record<HandledCallbackStatus, string> = {
  done: 'Đã gọi',
  unreachable: 'Không liên lạc được',
};

// Số điện thoại hoặc chữ thay thế khi job đã xóa số. Dùng cho tên truy cập và câu thông báo.
export function phoneOrDeletedLabel(request: CallbackRequest): string {
  return request.phone ?? DELETED_PHONE_LABEL;
}
