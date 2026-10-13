import { LOW_STOCK_THRESHOLD } from '../../../shared/catalog/inventory';

// Loại movement mà API trả về. Chỉ `manual` và `return` dùng được khi điều chỉnh tay.
export type MovementType = 'manual' | 'order_reserve' | 'order_release' | 'return';
export type AdjustType = 'manual' | 'return';

// Cùng giới hạn với `AdjustStockRequestValidator` của backend.
export const MAX_REASON_LENGTH = 500;

// Một dòng lịch sử tồn kho của SKU.
export interface InventoryMovement {
  id: string;
  delta: number;
  type: MovementType;
  orderId: string | null;
  actorId: string | null;
  reason: string;
  createdAt: string;
}

export interface PagedList<TItem> {
  items: TItem[];
  total: number;
  page: number;
  limit: number;
}

// Kết quả điều chỉnh: tồn kho mới và movement vừa ghi.
export interface AdjustStockResult {
  skuId: string;
  stock: number;
  movement: InventoryMovement;
}

// SKU đang bật có tồn kho dưới hoặc bằng ngưỡng sắp hết hàng.
export interface LowStockSku {
  skuId: string;
  code: string;
  productId: string;
  productName: string;
  optionValues: Record<string, string>;
  stock: number;
}

// Giá trị người dùng nhập. `delta` giữ dạng chuỗi để phân biệt ô trống với số 0.
export interface AdjustmentForm {
  type: AdjustType;
  delta: string;
  reason: string;
}

export interface AdjustmentErrors {
  delta?: string;
  reason?: string;
}

const movementTypeLabels: Record<MovementType, string> = {
  manual: 'Điều chỉnh',
  return: 'Khách trả hàng',
  order_reserve: 'Giữ hàng cho đơn',
  order_release: 'Trả hàng về kho',
};

// Thứ tự các loại trong ô chọn lọc: việc nhân viên làm tay trước, rồi tới biến động do đơn hàng. Giá trị là mã gửi cho API.
const MOVEMENT_TYPE_ORDER: MovementType[] = ['manual', 'order_reserve', 'order_release', 'return'];

export const MOVEMENT_TYPE_OPTIONS = MOVEMENT_TYPE_ORDER.map((type) => ({
  value: type,
  label: movementTypeLabels[type],
}));

// Chỉ nhận chuỗi số nguyên có dấu tùy chọn. `Number()` cho phép dạng như `1e3` hoặc chuỗi rỗng nên không đủ chặt.
const integerPattern = /^[+-]?\d+$/;

function parseDelta(text: string): number | null {
  const trimmed = text.trim();
  return integerPattern.test(trimmed) ? Number(trimmed) : null;
}

// Kiểm form với cùng luật của backend, để lỗi hiện ngay dưới ô trước khi gửi request.
export function validateAdjustment(form: AdjustmentForm): AdjustmentErrors {
  const errors: AdjustmentErrors = {};
  const delta = parseDelta(form.delta);
  if (delta === null) {
    errors.delta = 'Số lượng thay đổi phải là số nguyên.';
  } else if (delta === 0) {
    errors.delta = 'Số lượng thay đổi phải khác 0.';
  } else if (form.type === 'return' && delta < 0) {
    // Trả hàng là nhập kho lại, nên số lượng âm vô nghĩa.
    errors.delta = 'Khách trả hàng phải có số lượng lớn hơn 0.';
  }

  // Backend đo độ dài sau khi cắt khoảng trắng, nên client đo giống vậy.
  const reason = form.reason.trim();
  if (!reason) {
    errors.reason = 'Lý do là bắt buộc.';
  } else if (reason.length > MAX_REASON_LENGTH) {
    errors.reason = `Lý do không được dài quá ${MAX_REASON_LENGTH} ký tự.`;
  }

  return errors;
}

// Gọi sau khi `validateAdjustment` không còn lỗi. `delta` luôn là số nguyên ở bước này.
export function toAdjustmentBody(form: AdjustmentForm) {
  return { delta: Number(form.delta.trim()), reason: form.reason.trim(), type: form.type };
}

// Mã loại lạ giữ nguyên để người dùng vẫn thấy được giá trị API trả về.
export function movementTypeLabel(type: string): string {
  return movementTypeLabels[type as MovementType] ?? type;
}

// Dấu trừ là ký tự ASCII thường, theo quy ước của repo.
export function formatDelta(delta: number): string {
  if (delta > 0) return `+${delta}`;
  return String(delta);
}

// Số tồn sau điều chỉnh để dialog hiện trước. Trả null khi số lượng chưa hợp lệ.
export function stockAfterAdjustment(currentStock: number, deltaText: string): number | null {
  const delta = parseDelta(deltaText);
  return delta === null ? null : currentStock + delta;
}

const vietnamTime = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Ho_Chi_Minh',
  hourCycle: 'h23',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
});

// Hiện giờ Việt Nam dạng `HH:mm dd/MM/yyyy`, không phụ thuộc múi giờ của máy xem.
export function formatMovementTime(isoTime: string): string {
  // API trả giờ UTC. Chuỗi thiếu múi giờ sẽ bị `Date` hiểu là giờ máy, nên thêm `Z` trước.
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/.test(isoTime);
  const parts = vietnamTime.formatToParts(new Date(hasZone ? isoTime : `${isoTime}Z`));
  const get = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((part) => part.type === type)?.value ?? '';
  return `${get('hour')}:${get('minute')} ${get('day')}/${get('month')}/${get('year')}`;
}

export interface StockBadge {
  label: string;
  className: string;
}

// Nhãn và màu badge theo tồn kho. Dùng chung ngưỡng với bộ lọc sắp hết hàng của danh sách sản phẩm.
export function stockBadge(stock: number): StockBadge {
  if (stock <= 0) return { label: 'Hết hàng', className: 'badge badge-bad' };
  if (stock <= LOW_STOCK_THRESHOLD) return { label: 'Sắp hết', className: 'badge badge-warn' };
  return { label: 'Còn hàng', className: 'badge badge-ok' };
}

// Tên tổ hợp tùy chọn để hiện trong bảng, giống cách bảng biến thể gọi tên.
export function describeOptionValues(optionValues: Record<string, string>): string {
  return Object.values(optionValues).join(' / ') || 'Mặc định';
}
