import { ApiError } from '../../../shared/api';
import { getErrorMessage } from '../../../shared/error-message';

// Chuyển đổi mã lỗi từ API thành thông điệp tiếng Việt rõ ràng cho người dùng.
export function toCatalogErrorMessage(err: unknown, fallbackMessage: string): string {
  // Ánh xạ các mã lỗi nghiệp vụ đặc thù của catalog sang câu giải thích cụ thể.
  if (err instanceof ApiError) {
    if (err.code === 'ATTRIBUTE_IN_USE') {
      return 'Thuộc tính đang được sản phẩm sử dụng nên không thể xóa hoặc đổi kiểu dữ liệu.';
    }
    if (err.code === 'BRAND_HAS_PRODUCTS') {
      return 'Thương hiệu đang có sản phẩm, không thể xóa.';
    }
    if (err.code === 'CATEGORY_HAS_CHILDREN') {
      return 'Danh mục đang có danh mục con, không thể xóa.';
    }
    if (err.code === 'CATEGORY_HAS_PRODUCTS') {
      return 'Danh mục đang có sản phẩm, không thể xóa.';
    }
  }

  // Câu của API và câu kiểm dữ liệu trong form đã là tiếng Việt. Lỗi JS hay lỗi thư viện hiện câu dự phòng.
  return getErrorMessage(err, fallbackMessage);
}
