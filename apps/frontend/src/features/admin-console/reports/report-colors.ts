// Màu phân loại của biểu đồ, lấy từ token trong `tokens.css`.
// Màu chỉ hỗ trợ nhận biết. Nhãn phần trăm và bảng số liệu vẫn đủ nghĩa khi người đọc không phân biệt màu.
export const REPORT_SERIES_COLORS = [
  'var(--color-chart-1)',
  'var(--color-chart-2)',
  'var(--color-chart-3)',
  'var(--color-chart-4)',
  'var(--color-chart-5)',
  'var(--color-chart-6)',
] as const;

export const REPORT_OTHER_COLOR = 'var(--color-chart-other)';

// Nhóm "Khác" dùng màu trung tính để không tranh với thương hiệu thật. Các nhóm khác theo thứ tự lát.
export function reportSeriesColor(index: number, isNeutral = false): string {
  if (isNeutral) return REPORT_OTHER_COLOR;
  // Nhiều hơn sáu nhóm thì lặp bảng màu. Bảng số liệu vẫn phân biệt được từng nhóm.
  return REPORT_SERIES_COLORS[index % REPORT_SERIES_COLORS.length] ?? REPORT_SERIES_COLORS[0];
}
