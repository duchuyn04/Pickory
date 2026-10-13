import { normalizeForSearch } from '../../../shared/search-text';

// Phần của một danh mục mà việc lọc và dựng cây cần. Nhận mọi kiểu có đủ các trường này.
interface CategoryNode {
  id: string;
  parentId: string | null;
  name: string;
  slug: string;
  position: number;
}

// Từ khóa khớp khi tên hoặc slug chứa từ khóa, sau khi cả hai bỏ dấu và hạ chữ thường.
function matchesKeyword(fields: string[], normalizedKeyword: string): boolean {
  return fields.some((field) => normalizeForSearch(field).includes(normalizedKeyword));
}

// Lọc danh sách phẳng danh mục theo tên và slug. Giữ thứ tự đầu vào.
export function filterCategoryTree<T extends CategoryNode>(categories: T[], keyword: string): T[] {
  const normalizedKeyword = normalizeForSearch(keyword);
  // Từ khóa rỗng nghĩa là chưa lọc. Trả đúng danh sách vào để màn không dựng lại cây.
  if (normalizedKeyword === '') return categories;

  const matchedIds = new Set(
    categories
      .filter((category) => matchesKeyword([category.name, category.slug], normalizedKeyword))
      .map((category) => category.id),
  );
  // Danh mục gốc có con khớp phải hiện để con không đứng một mình, ngoài cây.
  const parentIdsOfMatchedChildren = new Set(
    categories
      .filter((category) => category.parentId !== null && matchedIds.has(category.id))
      .map((category) => category.parentId),
  );

  return categories.filter((category) => {
    if (matchedIds.has(category.id)) return true;
    // Gốc không khớp nhưng có con khớp: giữ làm khung cho con khớp.
    if (parentIdsOfMatchedChildren.has(category.id)) return true;
    // Gốc khớp thì giữ mọi con, vì con thuộc nhánh của danh mục người dùng đang tìm.
    return category.parentId !== null && matchedIds.has(category.parentId);
  });
}

export interface CategoryTree<T extends CategoryNode> {
  roots: T[];
  childrenByParent: Record<string, T[]>;
}

function compareByPositionThenName(a: CategoryNode, b: CategoryNode): number {
  return a.position - b.position || a.name.localeCompare(b.name);
}

// Dựng cây hai cấp từ danh sách phẳng. Gốc và con đều sắp theo vị trí rồi theo tên.
export function buildCategoryTree<T extends CategoryNode>(categories: T[]): CategoryTree<T> {
  const roots = categories.filter((category) => !category.parentId).sort(compareByPositionThenName);

  const childrenByParent: Record<string, T[]> = {};
  for (const category of categories) {
    if (!category.parentId) continue;
    const siblings = childrenByParent[category.parentId] ?? [];
    siblings.push(category);
    childrenByParent[category.parentId] = siblings;
  }
  for (const siblings of Object.values(childrenByParent)) {
    siblings.sort(compareByPositionThenName);
  }

  return { roots, childrenByParent };
}

// Lọc thương hiệu theo tên và slug.
export function filterBrands<T extends { name: string; slug: string }>(
  brands: T[],
  keyword: string,
): T[] {
  const normalizedKeyword = normalizeForSearch(keyword);
  if (normalizedKeyword === '') return brands;
  return brands.filter((brand) => matchesKeyword([brand.name, brand.slug], normalizedKeyword));
}
