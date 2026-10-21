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
