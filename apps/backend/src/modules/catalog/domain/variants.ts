// Giới hạn số lượng tổ hợp biến thể tối đa cho phép của một sản phẩm.
export const MAX_VARIANT_COMBINATIONS = 100;

// Định nghĩa một trục tùy chọn của sản phẩm.
export interface ProductOption {
  name: string;
  values: string[];
}

// Bảng ánh xạ giá trị tùy chọn của từng biến thể SKU.
export type VariantOptionValues = Record<string, string>;

// Sinh toàn bộ tổ hợp biến thể từ danh sách các trục tùy chọn.
export function buildVariantCombinations(options: ProductOption[]): VariantOptionValues[] {
  // Chặn vượt quá giới hạn 3 trục theo kiến trúc hệ thống để tránh bùng nổ tổ hợp SKU.
  if (options.length > 3) {
    throw new Error('Sản phẩm chỉ được có tối đa 3 trục tùy chọn.');
  }

  // Sản phẩm không có tùy chọn vẫn có đúng 1 biến thể mặc định với tập giá trị rỗng.
  if (options.length === 0) {
    return [{}];
  }

  const seenAxisNames = new Set<string>();
  const sanitizedAxes: Array<{ name: string; values: string[] }> = [];
  let estimatedCombinations = 1;

  for (const option of options) {
    // Chặn tên trục rỗng sau khi cắt khoảng trắng để giao diện luôn có tên trường.
    if (!option.name || option.name.trim().length === 0) {
      throw new Error('Tên trục tùy chọn không được để trống.');
    }

    const trimmedName = option.name.trim();

    // Chặn trùng lặp tên trục tùy chọn để tránh ghi đè khóa trong optionValues.
    if (seenAxisNames.has(trimmedName)) {
      throw new Error(`Tên trục tùy chọn "${trimmedName}" bị trùng lặp.`);
    }
    seenAxisNames.add(trimmedName);

    // Chặn trục tùy chọn không có giá trị để không làm mất danh sách biến thể.
    if (!option.values || option.values.length === 0) {
      throw new Error(`Trục tùy chọn "${trimmedName}" phải có ít nhất 1 giá trị.`);
    }

    const seenValues = new Set<string>();
    const sanitizedValues: string[] = [];

    for (const rawValue of option.values) {
      // Chặn giá trị tùy chọn rỗng sau khi cắt khoảng trắng để giữ giá trị có nghĩa.
      if (!rawValue || rawValue.trim().length === 0) {
        throw new Error(`Giá trị của trục tùy chọn "${trimmedName}" không được để trống.`);
      }

      const trimmedValue = rawValue.trim();

      // Chặn giá trị trùng lặp trong cùng một trục để không sinh biến thể trùng nhau.
      if (seenValues.has(trimmedValue)) {
        throw new Error(
          `Giá trị "${trimmedValue}" của trục tùy chọn "${trimmedName}" bị trùng lặp.`,
        );
      }

      seenValues.add(trimmedValue);
      sanitizedValues.push(trimmedValue);
    }

    estimatedCombinations *= sanitizedValues.length;
    sanitizedAxes.push({ name: trimmedName, values: sanitizedValues });
  }

  // Chặn bùng nổ số lượng biến thể quá ngưỡng tối đa cho phép để bảo đảm hiệu năng.
  if (estimatedCombinations > MAX_VARIANT_COMBINATIONS) {
    throw new Error(
      `Tổng số tổ hợp biến thể vượt quá giới hạn tối đa ${MAX_VARIANT_COMBINATIONS}.`,
    );
  }

  // Khởi tạo danh sách tích Descartes với một bản ghi rỗng ban đầu.
  let currentCombinations: VariantOptionValues[] = [{}];

  for (const axis of sanitizedAxes) {
    const nextCombinations: VariantOptionValues[] = [];

    // Ghép từng tổ hợp hiện có với từng giá trị mới của trục tiếp theo.
    for (const combination of currentCombinations) {
      for (const value of axis.values) {
        nextCombinations.push({
          ...combination,
          [axis.name]: value,
        });
      }
    }

    currentCombinations = nextCombinations;
  }

  return currentCombinations;
}
