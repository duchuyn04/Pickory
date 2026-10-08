import { ApiError } from '../../../shared/api';

export const PRODUCT_STATUSES = ['draft', 'active', 'hidden', 'discontinued'] as const;
export type ProductStatus = (typeof PRODUCT_STATUSES)[number];

// Danh sách sản phẩm và trang sửa dùng chung nhãn này, để hai màn không hiện hai tên cho một trạng thái.
export const PRODUCT_STATUS_NAMES: Record<ProductStatus, string> = {
  draft: 'Bản nháp',
  active: 'Đang bán',
  hidden: 'Đã ẩn',
  discontinued: 'Ngừng bán',
};

// Màu badge theo trạng thái: đang bán là xanh, đã ẩn là vàng, ngừng bán là đỏ, bản nháp trung tính.
export const PRODUCT_STATUS_BADGE_CLASSES: Record<ProductStatus, string> = {
  draft: 'badge',
  active: 'badge badge-ok',
  hidden: 'badge badge-warn',
  discontinued: 'badge badge-bad',
};

// Bảng này phải khớp `AllowedTransitions` trong `ProductPublishing.cs`. Cặp chuyển sang chính nó bị bỏ vì không đổi gì.
const ALLOWED_TARGET_STATUSES: Record<ProductStatus, ProductStatus[]> = {
  draft: ['active'],
  active: ['hidden', 'discontinued'],
  hidden: ['active', 'discontinued'],
  discontinued: ['active', 'hidden'],
};

// Trạng thái lạ từ API không có nút nào, vì giao diện không biết backend cho phép chuyển đi đâu.
export function getAllowedTargetStatuses(from: string): ProductStatus[] {
  return ALLOWED_TARGET_STATUSES[from as ProductStatus] ?? [];
}

// Nhãn nút theo trạng thái đích. Riêng `active` đổi theo nguồn: lần đầu là đăng bán, các lần sau là bán lại.
export function getStatusActionLabel(from: string, to: ProductStatus): string {
  if (to === 'active') return from === 'draft' ? 'Đăng bán' : 'Bán lại';
  if (to === 'hidden') return 'Ẩn sản phẩm';
  if (to === 'discontinued') return 'Ngừng kinh doanh';
  return PRODUCT_STATUS_NAMES[to];
}

// API trả `details` của lỗi 422 là mảng chuỗi lý do, khác dạng `{ key, message }` của lỗi thông số.
// Đọc qua `unknown` để bỏ phần tử không phải chuỗi thay vì hiện chữ rỗng.
export function readNotPublishableReasons(error: unknown): string[] {
  if (!(error instanceof ApiError) || error.code !== 'PRODUCT_NOT_PUBLISHABLE') return [];
  const details: unknown[] = error.details ?? [];
  return details.filter((detail): detail is string => typeof detail === 'string');
}
