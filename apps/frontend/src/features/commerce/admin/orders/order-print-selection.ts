import { ORDER_FILTER_NAMES } from './admin-orders-api';

// Giới hạn tối đa số đơn mỗi lần in theo quy định của API dữ liệu hóa đơn.
export const MAX_ORDERS_PER_PRINT = 50;

export type SelectAllState = 'none' | 'some' | 'all';

// Tạo khóa định danh cho bộ lọc và trang hiện tại để phát hiện lúc cần xóa lựa chọn.
export function buildOrderListSelectionKey(values: Record<string, string>, page: number): string {
  // Dùng JSON để giá trị lọc có ký tự đặc biệt không làm trùng khóa với tổ hợp khác.
  const filterTuple = ORDER_FILTER_NAMES.map((name) => values[name] ?? '');
  return JSON.stringify([...filterTuple, page]);
}

// Lọc các mã còn hiển thị theo đúng thứ tự dòng từ trên xuống để URL in khớp bảng.
export function getVisibleSelectedCodes(
  selectedCodes: ReadonlySet<string>,
  visibleCodes: readonly string[],
  limit = MAX_ORDERS_PER_PRINT,
): string[] {
  const result: string[] = [];
  // Duyệt theo danh sách đang hiển thị để giữ thứ tự từ trên xuống và bỏ mã không còn trên trang.
  for (const code of visibleCodes) {
    if (!selectedCodes.has(code)) {
      continue;
    }
    result.push(code);
    // Dừng khi đủ giới hạn để bảo đảm danh sách gửi đi không vượt quá ngưỡng cho phép.
    if (result.length >= limit) {
      break;
    }
  }
  return result;
}

// Đổi trạng thái chọn của một đơn trong danh sách đang hiển thị.
export function toggleOrderSelection(
  selectedCodes: ReadonlySet<string>,
  code: string,
  visibleCodes: readonly string[],
  limit = MAX_ORDERS_PER_PRINT,
): Set<string> {
  // Loại bỏ mã không còn trên trang trước khi kiểm tra số lượng đang chọn.
  const next = new Set(getVisibleSelectedCodes(selectedCodes, visibleCodes, limit));
  // Luôn cho phép bỏ chọn ngay cả khi đã chạm ngưỡng tối đa.
  if (next.has(code)) {
    next.delete(code);
    return next;
  }
  // Bỏ qua mã không thuộc trang hiện tại hoặc khi đã đủ số đơn tối đa.
  if (!visibleCodes.includes(code) || next.size >= limit) {
    return next;
  }
  next.add(code);
  return next;
}

// Tính trạng thái của ô chọn tất cả trên tiêu đề cột.
export function getSelectAllState(
  selectedCount: number,
  visibleCount: number,
  limit = MAX_ORDERS_PER_PRINT,
): SelectAllState {
  // Số đơn tối đa có thể chọn trên một trang không vượt quá giới hạn mỗi lần in.
  const targetCount = Math.min(visibleCount, limit);
  if (targetCount === 0 || selectedCount === 0) {
    return 'none';
  }
  if (selectedCount >= targetCount) {
    return 'all';
  }
  return 'some';
}

// Bật hoặc tắt chọn toàn bộ đơn hợp lệ trong trang hiện tại.
export function toggleSelectAllOrders(
  selectedCodes: ReadonlySet<string>,
  visibleCodes: readonly string[],
  limit = MAX_ORDERS_PER_PRINT,
): Set<string> {
  const visibleSelected = getVisibleSelectedCodes(selectedCodes, visibleCodes, limit);
  const currentState = getSelectAllState(visibleSelected.length, visibleCodes.length, limit);
  // Bỏ chọn hết khi đã tích đủ mức cho phép của trang.
  if (currentState === 'all') {
    return new Set();
  }
  // Chỉ lấy tối đa `limit` đơn đầu theo thứ tự hiển thị nếu trang có nhiều dòng hơn.
  return new Set(visibleCodes.slice(0, limit));
}
