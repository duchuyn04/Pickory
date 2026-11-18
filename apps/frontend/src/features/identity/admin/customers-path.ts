// Tên tham số bộ lọc trên URL. Trùng tên tham số của `GET /api/admin/customers`.
export const CUSTOMER_FILTER_NAMES = ['search', 'status', 'emailVerified'] as const;

// Giá trị bộ lọc đọc từ URL, mỗi tên bộ lọc một chuỗi.
type CustomerFilterValues = Record<(typeof CUSTOMER_FILTER_NAMES)[number], string>;

// Dựng đường dẫn từ bộ lọc trên URL. Chỉ gửi tham số có giá trị, và chỉ gửi `page` khi lớn hơn 1.
function buildCustomersPath(basePath: string, values: CustomerFilterValues, page = 1): string {
  // `URLSearchParams` mã hóa ký tự đặc biệt trong từ khóa (như & hay #) để URL không sai.
  const params = new URLSearchParams();
  for (const name of CUSTOMER_FILTER_NAMES) {
    if (values[name] !== '') params.set(name, values[name]);
  }
  if (page > 1) params.set('page', String(page));
  const queryString = params.toString();
  return queryString ? `${basePath}?${queryString}` : basePath;
}

// Đường dẫn của danh sách khách hàng theo bộ lọc và trang.
export function buildAdminCustomersPath(values: CustomerFilterValues, page: number): string {
  return buildCustomersPath('/api/admin/customers', values, page);
}

// Đường dẫn tải file Excel. Dùng chung bộ lọc với danh sách, nhưng không có `page` vì file chứa mọi dòng khớp.
export function buildAdminCustomersExportPath(values: CustomerFilterValues): string {
  return buildCustomersPath('/api/admin/exports/customers', values);
}
