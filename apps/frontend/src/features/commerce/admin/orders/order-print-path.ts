// Dựng URL mở trang in hóa đơn từ danh sách mã đơn đã chọn.
export function buildOrderPrintPath(codes: readonly string[]): string {
  // Trả đường dẫn gốc khi không có mã nào để tránh tham số `codes` rỗng trên URL.
  if (codes.length === 0) {
    return '/admin/orders/print';
  }
  // Mã hóa từng mã riêng nhưng giữ dấu phẩy để trang in tách đúng từng đơn.
  const encodedCodes = codes.map((code) => encodeURIComponent(code)).join(',');
  return `/admin/orders/print?codes=${encodedCodes}`;
}
