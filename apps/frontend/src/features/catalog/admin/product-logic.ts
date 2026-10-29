import {
  resolveCategoryAttributes,
  type AttributeDefinition,
} from '../../../shared/catalog/attributes';

// Số trục tùy chọn tối đa cho phép trên một sản phẩm.
export const MAX_AXES = 3;

// Giới hạn số lượng tổ hợp biến thể tối đa để đảm bảo hiệu năng.
export const MAX_COMBINATIONS = 100;

// Trục tùy chọn sản phẩm gồm tên trục và danh sách các giá trị tương ứng.
export interface ProductOption {
  name: string;
  values: string[];
}

// Biến thể sản phẩm dùng trong giao diện form quản trị.
export interface ProductVariant {
  id?: string;
  code?: string;
  optionValues: Record<string, string>;
  // Ô nhập giá trong form cho phép để trống chuỗi rỗng khi vừa thêm tổ hợp mới.
  price: number | string;
  compareAtPrice?: number | string | null;
  // Số lượng tồn kho hiện tại. `stockEdited` cho biết admin đã sửa ô này nên payload mới gửi `stock`.
  stock?: number;
  stockEdited?: boolean;
  enabled: boolean;
  weightGrams?: number | string | null;
  lengthCm?: number | string | null;
  widthCm?: number | string | null;
  heightCm?: number | string | null;
}

// Cấu trúc biến thể gửi trong payload tạo hoặc cập nhật sản phẩm.
export interface ProductVariantRequest {
  optionValues?: Record<string, string>;
  code?: string | null;
  price: number;
  compareAtPrice?: number | null;
  // Chỉ gửi khi admin sửa ô số lượng. Bỏ trống để máy chủ giữ nguyên tồn kho hiện tại.
  stock?: number;
  enabled: boolean;
  weightGrams?: number | null;
  lengthCm?: number | null;
  widthCm?: number | null;
  heightCm?: number | null;
}

// Node danh mục trong danh sách danh mục phục vụ tính toán thuộc tính kỹ thuật.
export interface CategoryNode {
  id: string;
  parentId?: string | null;
  name?: string;
  attributes?: AttributeDefinition[] | null;
}

// Tạo chuỗi khóa đại diện cho tổ hợp thuộc tính không phụ thuộc vào thứ tự khóa.
export function toCombinationKey(optionValues: Record<string, string> | null | undefined): string {
  // Không có trục tùy chọn phải khớp khóa của đối tượng rỗng khi ghép biến thể mặc định.
  if (!optionValues) {
    return '[]';
  }

  // Sắp xếp khóa chữ cái để đảm bảo thứ tự so sánh luôn nhất quán.
  const sortedKeys = Object.keys(optionValues).sort();
  const pairs = sortedKeys.map((key) => [key, optionValues[key]]);
  return JSON.stringify(pairs);
}

// Sinh danh sách tổ hợp biến thể theo tích Descartes từ các trục tùy chọn.
export function buildVariantCombinations(options: ProductOption[]): Record<string, string>[] {
  // Giới hạn số trục tùy chọn để ngăn số lượng SKU tăng cấp số nhân.
  if (options.length > MAX_AXES) {
    throw new Error(`Sản phẩm chỉ được có tối đa ${MAX_AXES} trục tùy chọn.`);
  }

  // Sản phẩm không có trục tùy chọn có đúng một biến thể mặc định với thuộc tính rỗng.
  if (options.length === 0) {
    return [{}];
  }

  // Chuẩn hóa và xác thực dữ liệu của từng trục tùy chọn.
  const sanitizedAxes = sanitizeAxes(options);

  // Đếm trước tổng số tổ hợp để phát hiện sớm trường hợp vượt ngưỡng.
  const totalCount = sanitizedAxes.reduce((count, axis) => count * axis.values.length, 1);
  if (totalCount > MAX_COMBINATIONS) {
    throw new Error(`Tổng số tổ hợp biến thể vượt quá giới hạn tối đa ${MAX_COMBINATIONS}.`);
  }

  // Tích Descartes: nhân từng tổ hợp hiện có với các giá trị của trục tiếp theo.
  let combinations: Record<string, string>[] = [{}];
  for (const axis of sanitizedAxes) {
    const nextCombinations: Record<string, string>[] = [];
    for (const combination of combinations) {
      for (const value of axis.values) {
        nextCombinations.push({
          ...combination,
          [axis.name]: value,
        });
      }
    }
    combinations = nextCombinations;
  }

  return combinations;
}

// Đồng bộ danh sách biến thể với các tổ hợp mới sinh trong khi giữ nguyên dữ liệu cũ.
export function mergeProductVariants(
  combinations: Record<string, string>[],
  existingVariants?: ProductVariant[] | null,
): ProductVariant[] {
  // Lập bản đồ tra cứu biến thể cũ theo chuỗi khóa tổ hợp để truy xuất nhanh.
  const lookup = new Map<string, ProductVariant>();
  if (existingVariants && existingVariants.length > 0) {
    for (const variant of existingVariants) {
      const key = toCombinationKey(variant.optionValues);
      if (!lookup.has(key)) {
        lookup.set(key, variant);
      }
    }
  }

  // Khớp từng tổ hợp với dữ liệu cũ hoặc khởi tạo giá trị mặc định cho tổ hợp mới.
  return combinations.map((combination) => {
    const key = toCombinationKey(combination);
    const existing = lookup.get(key);

    // Biến thể đã tồn tại giữ nguyên mã, giá bán và các thông số kích thước.
    if (existing) {
      return {
        id: existing.id,
        code: existing.code ?? '',
        optionValues: combination,
        price: existing.price !== undefined ? existing.price : '',
        compareAtPrice: existing.compareAtPrice !== undefined ? existing.compareAtPrice : null,
        stock: existing.stock ?? 0,
        // Giữ cờ đã sửa để lần gửi form vẫn gửi số đích. Biến thể chưa sửa không có khóa này.
        ...(existing.stockEdited ? { stockEdited: true } : {}),
        enabled: existing.enabled ?? true,
        weightGrams: existing.weightGrams !== undefined ? existing.weightGrams : null,
        lengthCm: existing.lengthCm !== undefined ? existing.lengthCm : null,
        widthCm: existing.widthCm !== undefined ? existing.widthCm : null,
        heightCm: existing.heightCm !== undefined ? existing.heightCm : null,
      };
    }

    // Tổ hợp mới tạo để trống giá bán và đặt tồn kho bằng 0 ở chế độ chỉ xem.
    return {
      id: undefined,
      code: '',
      optionValues: { ...combination },
      price: '',
      compareAtPrice: null,
      stock: 0,
      enabled: true,
      weightGrams: null,
      lengthCm: null,
      widthCm: null,
      heightCm: null,
    };
  });
}

// Lấy danh sách thuộc tính kỹ thuật đầy đủ của danh mục lá bao gồm thuộc tính kế thừa.
export function getLeafCategoryAttributes(
  categoryId: string | null | undefined,
  categories: CategoryNode[],
): AttributeDefinition[] {
  // Không có mã danh mục hoặc danh sách rỗng thì trả về mảng rỗng.
  if (!categoryId || !categories || categories.length === 0) {
    return [];
  }

  // Tìm danh mục được chỉ định trong danh sách danh mục đầu vào.
  const targetCategory = categories.find((item) => item.id === categoryId);
  if (!targetCategory) {
    return [];
  }

  // Truy vết chuỗi phân cấp từ danh mục gốc đến danh mục hiện tại.
  const visited = new Set<string>();
  const chain: CategoryNode[] = [];
  let current: CategoryNode | undefined = targetCategory;

  while (current && !visited.has(current.id)) {
    visited.add(current.id);
    chain.unshift(current);
    if (!current.parentId) {
      break;
    }
    current = categories.find((item) => item.id === current!.parentId);
  }

  // Kế thừa lần lượt thuộc tính từ gốc xuống lá thông qua hàm gộp chuẩn.
  let resolved: AttributeDefinition[] = [];
  for (const node of chain) {
    resolved = resolveCategoryAttributes(resolved, node.attributes ?? []);
  }

  return resolved;
}

// Chuyển đổi giá trị số tùy chọn về kiểu số hoặc null khi bỏ trống.
export function parseOptionalNumber(value: number | string | null | undefined): number | null {
  // Giá trị không xác định hoặc rỗng quy về null để gửi lên API.
  if (value === undefined || value === null || value === '') {
    return null;
  }
  const parsed = Number(value);
  // Tránh gửi giá trị NaN lên máy chủ khi chuỗi nhập không phải số.
  return Number.isNaN(parsed) ? null : parsed;
}

// Chuyển đổi biến thể giao diện thành payload gửi yêu cầu API hợp lệ.
export function toProductVariantRequest(variant: ProductVariant): ProductVariantRequest {
  // Chuyển đổi giá trị tiền tệ sang số và mặc định là 0 nếu chưa nhập.
  const priceNumber =
    typeof variant.price === 'number' ? variant.price : Number(variant.price) || 0;

  // Chỉ gửi `stock` khi admin sửa ô số lượng. Bỏ trống để máy chủ giữ nguyên số hiện tại.
  const stockRequest = variant.stockEdited ? { stock: variant.stock ?? 0 } : {};
  return {
    optionValues: variant.optionValues,
    code: variant.code && variant.code.trim().length > 0 ? variant.code.trim() : null,
    price: priceNumber,
    compareAtPrice: parseOptionalNumber(variant.compareAtPrice),
    ...stockRequest,
    enabled: variant.enabled ?? true,
    weightGrams: parseOptionalNumber(variant.weightGrams),
    lengthCm: parseOptionalNumber(variant.lengthCm),
    widthCm: parseOptionalNumber(variant.widthCm),
    heightCm: parseOptionalNumber(variant.heightCm),
  };
}

// Kiểm tra tính hợp lệ và chuẩn hóa danh sách các trục tùy chọn.
function sanitizeAxes(options: ProductOption[]): ProductOption[] {
  const sanitized: ProductOption[] = [];
  const seenNames = new Set<string>();

  for (const option of options) {
    // Tên trục tùy chọn không được để trống hoặc chỉ có khoảng trắng.
    if (!option.name || option.name.trim().length === 0) {
      throw new Error('Tên trục tùy chọn không được để trống.');
    }

    const trimmedName = option.name.trim();
    // Chặn trục có tên trùng lặp để tránh xung đột dữ liệu biến thể.
    if (seenNames.has(trimmedName)) {
      throw new Error(`Tên trục tùy chọn "${trimmedName}" bị trùng lặp.`);
    }
    seenNames.add(trimmedName);

    // Mỗi trục phải có ít nhất một giá trị để có thể sinh biến thể.
    const rawValues = option.values ?? [];
    if (rawValues.length === 0) {
      throw new Error(`Trục tùy chọn "${trimmedName}" phải có ít nhất 1 giá trị.`);
    }

    // Chuẩn hóa và kiểm tra danh sách giá trị của trục hiện tại.
    const sanitizedValues = sanitizeValues(trimmedName, rawValues);
    sanitized.push({
      name: trimmedName,
      values: sanitizedValues,
    });
  }

  return sanitized;
}

// Kiểm tra tính hợp lệ và loại bỏ trùng lặp khoảng trắng của các giá trị trong một trục.
function sanitizeValues(axisName: string, values: string[]): string[] {
  const sanitized: string[] = [];
  const seenValues = new Set<string>();

  for (const rawValue of values) {
    // Giá trị biến thể không được rỗng để khách hàng có thể chọn mua.
    if (!rawValue || rawValue.trim().length === 0) {
      throw new Error(`Giá trị của trục tùy chọn "${axisName}" không được để trống.`);
    }

    const trimmedValue = rawValue.trim();
    // Chặn giá trị trùng nhau trong cùng một trục để tránh sinh trùng biến thể.
    if (seenValues.has(trimmedValue)) {
      throw new Error(`Giá trị "${trimmedValue}" của trục tùy chọn "${axisName}" bị trùng lặp.`);
    }
    seenValues.add(trimmedValue);
    sanitized.push(trimmedValue);
  }

  return sanitized;
}
