// Tên tham số bộ lọc trên URL. Trùng tên tham số của `GET /api/admin/products`.
export const PRODUCT_FILTER_NAMES = ['search', 'status', 'categoryId', 'lowStock'] as const;

// Số sản phẩm mỗi trang của danh sách.
const PRODUCTS_PAGE_SIZE = 20;

// Bộ lọc đã đọc từ URL. `lowStock` là cờ bật tắt, còn trên URL của trang nó là chuỗi `true`.
export interface ProductPathFilters {
  search: string;
  status: string;
  categoryId: string;
  lowStock: boolean;
}

// Dựng đường dẫn từ bộ lọc. Có `page` thì thêm `page` và `limit` của danh sách.
function buildProductsPath(
  basePath: string,
  filters: ProductPathFilters,
  page: number | null,
): string {
  // `URLSearchParams` mã hóa từng bộ lọc để tên hoặc mã biến thể (SKU) có ký tự đặc biệt không đổi ý nghĩa URL.
  const params = new URLSearchParams();
  if (page !== null) {
    params.set('page', String(page));
    params.set('limit', String(PRODUCTS_PAGE_SIZE));
  }
  if (filters.search.trim()) params.set('search', filters.search.trim());
  if (filters.status) params.set('status', filters.status);
  if (filters.categoryId) params.set('categoryId', filters.categoryId);
  // API chỉ nhận chuỗi `true`. Cờ tắt thì không gửi tham số.
  if (filters.lowStock) params.set('lowStock', 'true');
  const queryString = params.toString();
  return queryString ? `${basePath}?${queryString}` : basePath;
}

// Đường dẫn của danh sách sản phẩm theo bộ lọc và trang.
export function buildAdminProductsPath(filters: ProductPathFilters, page: number): string {
  return buildProductsPath('/api/admin/products', filters, page);
}

// Đường dẫn tải file Excel. Dùng chung bộ lọc với danh sách, nhưng không có `page` và `limit` vì file chứa mọi dòng khớp.
export function buildAdminProductsExportPath(filters: ProductPathFilters): string {
  return buildProductsPath('/api/admin/exports/products', filters, null);
}
