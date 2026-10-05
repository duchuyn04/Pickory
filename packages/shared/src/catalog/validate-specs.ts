import type { AttributeDefinition } from './attributes';

// Cấu trúc lỗi khi giá trị thông số kỹ thuật không hợp lệ.
export interface SpecValidationError {
  key: string;
  message: string;
}

// Kết quả kiểm tra bộ thông số kỹ thuật của sản phẩm.
export interface SpecValidationResult {
  valid: boolean;
  errors: SpecValidationError[];
}

// Kiểm tra toàn bộ dữ liệu thông số theo danh sách thuộc tính của danh mục.
export function validateSpecs(
  specs: Record<string, unknown>,
  attributes: AttributeDefinition[],
): SpecValidationResult {
  const errors: SpecValidationError[] = [];

  // Chặn dữ liệu không phải đối tượng để tránh lỗi truy xuất thuộc tính.
  if (!specs || typeof specs !== 'object' || Array.isArray(specs)) {
    return {
      valid: false,
      errors: [
        {
          key: 'specs',
          message: 'Dữ liệu thông số phải là một đối tượng JSON hợp lệ.',
        },
      ],
    };
  }

  // Tạo bảng tra cứu thuộc tính theo khóa để tối ưu tốc độ tìm kiếm.
  const attributeMap = new Map<string, AttributeDefinition>();
  for (const attribute of attributes) {
    attributeMap.set(attribute.key, attribute);
  }

  // Chặn các khóa lạ ngoài danh mục để giữ sạch dữ liệu thông số kỹ thuật.
  for (const key of Object.keys(specs)) {
    if (!attributeMap.has(key)) {
      errors.push({
        key,
        message: `Khóa "${key}" không nằm trong bộ thuộc tính của danh mục.`,
      });
    }
  }

  // Kiểm tra từng thuộc tính được định nghĩa trong danh mục.
  for (const attribute of attributes) {
    // Dùng Object.hasOwn để tránh nhận nhầm các thuộc tính kế thừa từ prototype như constructor.
    const hasProperty = Object.hasOwn(specs, attribute.key);
    const value = hasProperty ? specs[attribute.key] : undefined;
    const isProvided = hasProperty && value !== undefined && value !== null;
    // Chặn thiếu thuộc tính bắt buộc để đảm bảo dữ liệu hiển thị tối thiểu.
    if (!isProvided) {
      if (attribute.required) {
        errors.push({
          key: attribute.key,
          message: `Thiếu thuộc tính bắt buộc "${attribute.label}" (${attribute.key}).`,
        });
      }
      continue;
    }

    // Kiểm tra tính hợp lệ của kiểu chuỗi văn bản.
    if (attribute.type === 'text') {
      if (typeof value !== 'string') {
        errors.push({
          key: attribute.key,
          message: `Thuộc tính "${attribute.label}" (${attribute.key}) phải là chuỗi văn bản.`,
        });
        continue;
      }

      // Chặn chuỗi rỗng để tránh lưu thông tin không có giá trị hiển thị.
      if (value.trim().length === 0) {
        errors.push({
          key: attribute.key,
          message: `Thuộc tính "${attribute.label}" (${attribute.key}) không được là chuỗi rỗng.`,
        });
      }
      continue;
    }

    // Kiểm tra tính hợp lệ của kiểu số.
    if (attribute.type === 'number') {
      // Chặn giá trị không phải số hợp lệ để phục vụ chức năng lọc theo khoảng giá trị.
      if (typeof value !== 'number' || Number.isNaN(value) || !Number.isFinite(value)) {
        errors.push({
          key: attribute.key,
          message: `Thuộc tính "${attribute.label}" (${attribute.key}) phải là số hợp lệ.`,
        });
      }
      continue;
    }

    // Kiểm tra tính hợp lệ của kiểu đúng sai boolean.
    if (attribute.type === 'boolean') {
      // Chặn sai kiểu dữ liệu boolean để bộ lọc checkbox hoạt động chính xác.
      if (typeof value !== 'boolean') {
        errors.push({
          key: attribute.key,
          message: `Thuộc tính "${attribute.label}" (${attribute.key}) phải là kiểu boolean.`,
        });
      }
      continue;
    }

    // Kiểm tra tính hợp lệ của kiểu danh mục chọn sẵn enum.
    if (attribute.type === 'enum') {
      // Chặn giá trị ngoài danh sách cho phép để bảo đảm tính toàn vẹn dữ liệu.
      const allowedOptions = attribute.options ?? [];
      if (typeof value !== 'string' || !allowedOptions.includes(value)) {
        errors.push({
          key: attribute.key,
          message: `Thuộc tính "${attribute.label}" (${attribute.key}) nhận giá trị không nằm trong danh sách lựa chọn.`,
        });
      }
      continue;
    }
  }

  return {
    valid: errors.length === 0,
    errors,
  };
}
