// Định dạng hiển thị của báo cáo. Số tiền là VND, ngày theo lịch Việt Nam.

const vndFormatter = new Intl.NumberFormat('vi-VN');
const countFormatter = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
const compactFormatter = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 });

// Hiện số lượt đếm, ví dụ lượt xem của một bước phễu. Bộ đếm là số nguyên, nên làm tròn phần lẻ.
export function formatCount(count: number): string {
  return countFormatter.format(count);
}

// Hiện số tiền đầy đủ. Bảng số liệu dùng hàm này để người đọc đối chiếu chính xác.
export function formatVnd(amount: number): string {
  return `${vndFormatter.format(Math.round(amount))} đ`;
}

// Nhãn trục ngắn để nhiều cột vẫn vừa màn hẹp. Bảng bên dưới luôn có số đầy đủ.
export function formatCompactVnd(amount: number): string {
  const absolute = Math.abs(amount);
  // Đơn vị tỷ và triệu giúp nhãn trục không chiếm hết chỗ của cột.
  if (absolute >= 1_000_000_000) return `${compactFormatter.format(amount / 1_000_000_000)} tỷ`;
  if (absolute >= 1_000_000) return `${compactFormatter.format(amount / 1_000_000)} tr`;
  return `${vndFormatter.format(Math.round(amount))} đ`;
}

// Đổi ngày lịch `YYYY-MM-DD` của API sang `dd/MM/yyyy`. API đã tính ngày theo giờ Việt Nam.
export function formatReportDate(dateOnly: string): string {
  const [year = '', month = '', day = ''] = dateOnly.split('-');
  return `${day}/${month}/${year}`;
}

// Nhãn tháng viết thường để ghép vào câu, ví dụ "tháng 5/2026". Bỏ số 0 ở tháng một chữ số.
export function formatReportMonth(month: string): string {
  return `tháng ${Number(month.slice(5, 7))}/${month.slice(0, 4)}`;
}

// Nhãn tháng đứng đầu ô chọn, nên viết hoa chữ đầu.
export function formatReportMonthTitle(month: string): string {
  return `Tháng ${Number(month.slice(5, 7))}/${month.slice(0, 4)}`;
}

// Phần trăm của một lát, nhận tỉ lệ từ 0 đến 1. Lát dưới 1% giữ dấu nhỏ hơn thay vì làm tròn thành 0%.
export function formatPercentValue(fraction: number): string {
  if (fraction > 0 && fraction < 0.01) return '<1%';
  return `${Math.round(fraction * 100)}%`;
}

// Phần trăm của một giá trị trên tổng. Tổng bằng 0 trả `0%` thay vì chia cho 0.
export function formatPercent(value: number, total: number): string {
  if (total <= 0) return '0%';
  return formatPercentValue(value / total);
}
