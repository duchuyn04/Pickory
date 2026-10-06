import type { AttributeDefinition } from '../../../shared/catalog/attributes';

// Cấu trúc phân tách danh sách thuộc tính của danh mục thành hai nhóm hiển thị.
export interface CategoryAttributeSections {
  inherited: AttributeDefinition[];
  own: AttributeDefinition[];
}

// Tách biệt thuộc tính kế thừa của cha và thuộc tính riêng của con để hiển thị rõ ràng trên giao diện.
export function splitCategoryAttributes(
  parentAttributes: AttributeDefinition[] | null | undefined,
  ownAttributes: AttributeDefinition[] | null | undefined,
): CategoryAttributeSections {
  // Bản sao mảng cha ngăn ngừa việc component con vô tình làm biến đổi dữ liệu của danh mục cha.
  const inherited = parentAttributes ? [...parentAttributes] : [];
  // Danh mục gốc hoặc danh mục con chưa cấu hình thuộc tính luôn nhận mảng rỗng thay vì null.
  const own = ownAttributes ? [...ownAttributes] : [];

  return { inherited, own };
}

// Chuyển chuỗi các tùy chọn phân tách bằng dấu phẩy thành mảng chuỗi đã cắt khoảng trắng.
export function parseEnumOptions(rawText: string | string[] | undefined): string[] {
  // Giữ nguyên mảng nếu dữ liệu đã ở dạng mảng chuẩn từ trước.
  if (Array.isArray(rawText)) {
    return rawText.map((item) => item.trim()).filter((item) => item.length > 0);
  }

  // Chuỗi rỗng hoặc undefined trả về mảng rỗng để validator phát hiện thiếu options.
  if (!rawText || typeof rawText !== 'string') {
    return [];
  }

  // Tách theo dấu phẩy và loại bỏ các phần tử rỗng do người dùng gõ thừa dấu phẩy.
  return rawText
    .split(',')
    .map((option) => option.trim())
    .filter((option) => option.length > 0);
}
