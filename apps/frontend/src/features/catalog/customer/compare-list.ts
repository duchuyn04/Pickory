// Danh sách so sánh lưu ở `localStorage`. UI-C3 dùng lại để mở trang so sánh.
export const COMPARE_LIST_LIMIT = 4;
export const COMPARE_STORAGE_KEY = 'pickory.compare';

export interface CompareList {
  // Danh mục của các sản phẩm trong danh sách. Null khi danh sách rỗng.
  categoryId: string | null;
  productIds: string[];
}

export type AddState = 'in-list' | 'can-add' | 'full' | 'category-mismatch';

export const emptyCompareList: CompareList = { categoryId: null, productIds: [] };

export function describeAddState(
  list: CompareList,
  categoryId: string,
  productId: string,
): AddState {
  // Kiểm "đã có" trước để nút của sản phẩm trong danh sách luôn bấm được và bỏ được, kể cả khi danh sách đầy.
  if (list.productIds.includes(productId)) return 'in-list';
  // So sánh chỉ có nghĩa giữa các sản phẩm cùng danh mục (cùng bộ thông số).
  if (list.categoryId !== null && list.categoryId !== categoryId) return 'category-mismatch';
  if (list.productIds.length >= COMPARE_LIST_LIMIT) return 'full';
  return 'can-add';
}

export function addToCompareList(
  list: CompareList,
  categoryId: string,
  productId: string,
): CompareList {
  // Mọi trạng thái khác `can-add` giữ nguyên danh sách, nên giao diện không cần tự kiểm lại.
  if (describeAddState(list, categoryId, productId) !== 'can-add') return list;
  return { categoryId, productIds: [...list.productIds, productId] };
}

export function removeFromCompareList(list: CompareList, productId: string): CompareList {
  const productIds = list.productIds.filter((id) => id !== productId);
  // Danh sách rỗng quên danh mục, để sản phẩm của danh mục khác được thêm vào.
  if (productIds.length === 0) return emptyCompareList;
  return { categoryId: list.categoryId, productIds };
}

export function startCompareList(categoryId: string, productId: string): CompareList {
  return { categoryId, productIds: [productId] };
}

// Dữ liệu trong `localStorage` do người dùng sửa được, nên phải kiểm dạng trước khi tin.
function parseStoredList(raw: string | null): CompareList {
  if (raw === null) return emptyCompareList;
  const parsed: unknown = JSON.parse(raw);
  if (typeof parsed !== 'object' || parsed === null) return emptyCompareList;

  const { categoryId, productIds } = parsed as Record<string, unknown>;
  if (typeof categoryId !== 'string' || !Array.isArray(productIds)) return emptyCompareList;

  // Bỏ id trùng và id sai kiểu. Sau đó cắt về giới hạn để dữ liệu cũ không vượt 4 sản phẩm.
  const uniqueIds = [...new Set(productIds.filter((id): id is string => typeof id === 'string'))];
  const limitedIds = uniqueIds.slice(0, COMPARE_LIST_LIMIT);
  if (limitedIds.length === 0) return emptyCompareList;
  return { categoryId, productIds: limitedIds };
}

export function readCompareList(storage: Storage | null): CompareList {
  try {
    return parseStoredList(storage?.getItem(COMPARE_STORAGE_KEY) ?? null);
  } catch {
    // Trình duyệt chặn storage hoặc JSON hỏng: coi như chưa có danh sách, không làm hỏng trang.
    return emptyCompareList;
  }
}

export function writeCompareList(storage: Storage | null, list: CompareList): void {
  try {
    storage?.setItem(COMPARE_STORAGE_KEY, JSON.stringify(list));
  } catch {
    // Không ghi được (chế độ riêng tư, hết dung lượng): danh sách chỉ sống trong phiên trang này.
  }
}
