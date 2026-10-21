// Thông tin phân trang của response. Trùng tên trường `page` và `limit` mà API trả về.
export interface RowNumberPage {
  page: number;
  limit: number;
}

// Số thứ tự (STT) đếm liên tục qua các trang, nên trang 2 tiếp số của trang 1.
// `index` là vị trí dòng trong trang, bắt đầu từ 0.
export function rowNumber(pageInfo: RowNumberPage, index: number): number {
  return (pageInfo.page - 1) * pageInfo.limit + index + 1;
}

// Số thứ tự trong cây hai cấp, với hai chỉ số bắt đầu từ 0: `1` cho gốc, `1.2` cho con thứ hai.
// Kết quả là chuỗi, vì kiểu số làm `1.10` thành `1.1`.
export function treeRowNumber(rootIndex: number, childIndex?: number): string {
  const rootNumber = rootIndex + 1;
  // Không có chỉ số con thì đây là mục gốc, nên chỉ có số của gốc.
  if (childIndex === undefined) return String(rootNumber);
  return `${rootNumber}.${childIndex + 1}`;
}
