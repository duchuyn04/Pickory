// Đọc tham số `codes` từ chuỗi query của URL và chuẩn hóa thành danh sách mã đơn duy nhất.
export function parseInvoiceOrderCodes(search: string): string[] {
  const rawCodes = new URLSearchParams(search).get('codes');
  // Thiếu tham số `codes` hoặc giá trị rỗng thì trả mảng rỗng để trang chặn gọi API.
  if (!rawCodes) {
    return [];
  }

  const uniqueCodes: string[] = [];
  const seenCodes = new Set<string>();

  for (const part of rawCodes.split(',')) {
    const trimmed = part.trim();
    // Bỏ phần tử rỗng phát sinh từ dấu phẩy thừa và giữ lại lần xuất hiện đầu của mỗi mã.
    if (!trimmed || seenCodes.has(trimmed)) {
      continue;
    }
    seenCodes.add(trimmed);
    uniqueCodes.push(trimmed);
  }

  return uniqueCodes;
}

// Dựng URL gọi API lấy dữ liệu in từ danh sách mã đơn đã chuẩn hóa.
export function buildOrderInvoicesApiPath(codes: string[]): string {
  // Mã hóa từng mã riêng và giữ dấu phẩy phân cách để backend tách đúng danh sách.
  const encodedCodes = codes.map((code) => encodeURIComponent(code)).join(',');
  return `/api/admin/order-invoices?codes=${encodedCodes}`;
}
