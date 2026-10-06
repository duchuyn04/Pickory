// Kiểu dữ liệu hỗ trợ cho thuộc tính thông số kỹ thuật.
export type AttributeType = 'text' | 'number' | 'enum' | 'boolean';

// Định nghĩa một thuộc tính thông số thuộc danh mục sản phẩm.
export interface AttributeDefinition {
  key: string;
  label: string;
  group: string;
  type: AttributeType;
  unit?: string;
  options?: string[];
  required: boolean;
  filterable: boolean;
}

// Cấu trúc lỗi khi định nghĩa thuộc tính không hợp lệ.
export interface AttributeDefinitionError {
  key?: string;
  message: string;
}

// Kết quả kiểm tra tính hợp lệ của định nghĩa thuộc tính.
export interface AttributeDefinitionValidationResult {
  valid: boolean;
  errors: AttributeDefinitionError[];
}

// Tập hợp 4 kiểu dữ liệu thuộc tính hợp lệ được hệ thống hỗ trợ.
const ALLOWED_ATTRIBUTE_TYPES: ReadonlySet<AttributeType> = new Set([
  'text',
  'number',
  'enum',
  'boolean',
]);

// Quy tắc đặt khóa thuộc tính: chữ cái thường đầu tiên, sau đó là chữ thường, số hoặc gạch dưới.
const ATTRIBUTE_KEY_PATTERN = /^[a-z][a-z0-9_]*$/;
// Kiểm tra tính hợp lệ của một định nghĩa thuộc tính đơn lẻ.
export function validateAttributeDefinition(
  attribute: AttributeDefinition,
): AttributeDefinitionValidationResult {
  const errors: AttributeDefinitionError[] = [];

  // Chặn khóa sai quy cách để giữ tính nhất quán khi lưu vào JSON và tạo query.
  if (!attribute.key || !ATTRIBUTE_KEY_PATTERN.test(attribute.key)) {
    errors.push({
      key: attribute.key,
      message: `Khóa thuộc tính "${attribute.key}" không đúng định dạng ^[a-z][a-z0-9_]*$.`,
    });
  }

  // Chặn nhãn rỗng để giao diện luôn có tên hiển thị cho người dùng.
  if (!attribute.label || attribute.label.trim().length === 0) {
    errors.push({
      key: attribute.key,
      message: 'Nhãn thuộc tính không được để trống.',
    });
  }

  // Chặn nhóm rỗng để bảng thông số luôn phân nhóm được.
  if (!attribute.group || attribute.group.trim().length === 0) {
    errors.push({
      key: attribute.key,
      message: 'Nhóm thuộc tính không được để trống.',
    });
  }
  // Chặn kiểu dữ liệu lạ ngoài 4 kiểu quy định để tránh lỗi validate thông số về sau.
  if (!ALLOWED_ATTRIBUTE_TYPES.has(attribute.type)) {
    errors.push({
      key: attribute.key,
      message: `Kiểu thuộc tính "${String(attribute.type)}" không hợp lệ, chỉ nhận text, number, enum hoặc boolean.`,
    });
  }

  // Bắt buộc danh sách lựa chọn cho kiểu enum và kiểm tra tính toàn vẹn của từng lựa chọn.
  if (attribute.type === 'enum') {
    if (!attribute.options || attribute.options.length === 0) {
      errors.push({
        key: attribute.key,
        message: `Thuộc tính "${attribute.key}" kiểu enum bắt buộc phải có danh sách options.`,
      });
    } else {
      const seenOptions = new Set<string>();
      for (const opt of attribute.options) {
        // Chặn lựa chọn rỗng sau khi cắt khoảng trắng để giao diện không hiện dòng trống.
        if (typeof opt !== 'string' || opt.trim().length === 0) {
          errors.push({
            key: attribute.key,
            message: `Tùy chọn của thuộc tính "${attribute.key}" không được để trống.`,
          });
          break;
        }

        const trimmedOption = opt.trim();
        // Chặn các lựa chọn trùng nhau sau khi cắt khoảng trắng để tránh xung đột giá trị.
        if (seenOptions.has(trimmedOption)) {
          errors.push({
            key: attribute.key,
            message: `Tùy chọn "${trimmedOption}" của thuộc tính "${attribute.key}" bị lặp lại.`,
          });
          break;
        }
        seenOptions.add(trimmedOption);
      }
    }
  }
  return {
    valid: errors.length === 0,
    errors,
  };
}

// Kiểm tra danh sách định nghĩa thuộc tính và phát hiện khóa trùng lặp.
export function validateAttributeDefinitions(
  attributes: AttributeDefinition[],
): AttributeDefinitionValidationResult {
  const errors: AttributeDefinitionError[] = [];
  const seenKeys = new Set<string>();

  for (const attribute of attributes) {
    // Kiểm tra từng thuộc tính theo quy chuẩn chung.
    const singleResult = validateAttributeDefinition(attribute);
    if (!singleResult.valid) {
      errors.push(...singleResult.errors);
    }

    // Chặn trùng khóa trong cùng một danh sách để tránh xung đột dữ liệu thông số.
    if (seenKeys.has(attribute.key)) {
      errors.push({
        key: attribute.key,
        message: `Khóa thuộc tính "${attribute.key}" bị lặp lại trong danh sách.`,
      });
    } else {
      seenKeys.add(attribute.key);
    }
  }

  return {
    valid: errors.length === 0,
    errors,
  };
}

// Gộp thuộc tính của danh mục cha và danh mục con.
export function resolveCategoryAttributes(
  parentAttributes: AttributeDefinition[] | null | undefined,
  childAttributes: AttributeDefinition[],
): AttributeDefinition[] {
  // Kiểm tra danh sách thuộc tính con trước khi kế thừa.
  const childValidation = validateAttributeDefinitions(childAttributes);
  if (!childValidation.valid) {
    const detail = childValidation.errors.map((err) => err.message).join(' ');
    throw new Error(`Định nghĩa thuộc tính danh mục con không hợp lệ: ${detail}`);
  }

  // Danh mục gốc không có cha thì chỉ trả về thuộc tính của chính nó.
  if (!parentAttributes || parentAttributes.length === 0) {
    return [...childAttributes];
  }

  // Kiểm tra danh sách thuộc tính cha để đảm bảo dữ liệu nguồn đúng chuẩn.
  const parentValidation = validateAttributeDefinitions(parentAttributes);
  if (!parentValidation.valid) {
    const detail = parentValidation.errors.map((err) => err.message).join(' ');
    throw new Error(`Định nghĩa thuộc tính danh mục cha không hợp lệ: ${detail}`);
  }

  // Tập hợp các khóa của cha để đối chiếu xung đột.
  const parentKeys = new Set(parentAttributes.map((attr) => attr.key));

  // Chặn trùng khóa giữa cha và con để không ghi đè thuộc tính kế thừa.
  for (const childAttr of childAttributes) {
    if (parentKeys.has(childAttr.key)) {
      throw new Error(
        `Trùng khóa thuộc tính "${childAttr.key}" giữa danh mục cha và danh mục con.`,
      );
    }
  }

  // Trả về thuộc tính cha trước, thuộc tính con sau để giữ thứ tự phân cấp.
  return [...parentAttributes, ...childAttributes];
}
