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

  // Khởi tạo danh sách tích Descartes với một bản ghi rỗng ban đầu.
  let currentCombinations: VariantOptionValues[] = [{}];

  for (const option of options) {
    // Chặn trục tùy chọn không có giá trị để không làm mất danh sách biến thể.
    if (!option.values || option.values.length === 0) {
      throw new Error(`Trục tùy chọn "${option.name}" phải có ít nhất 1 giá trị.`);
    }

    const nextCombinations: VariantOptionValues[] = [];

    // Ghép từng tổ hợp hiện có với từng giá trị mới của trục tiếp theo.
    for (const combination of currentCombinations) {
      for (const value of option.values) {
        nextCombinations.push({
          ...combination,
          [option.name]: value,
        });
      }
    }

    currentCombinations = nextCombinations;
  }

  return currentCombinations;
}
