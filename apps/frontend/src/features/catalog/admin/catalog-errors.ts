import { ApiError } from '../../../shared/api';

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
    if (err.message) {
      return err.message;
    }
  }

  // Trả thông điệp lỗi nguyên bản nếu đối tượng lỗi là Error thông thường.
  if (err instanceof Error && err.message) {
    return err.message;
  }

  return fallbackMessage;
}
