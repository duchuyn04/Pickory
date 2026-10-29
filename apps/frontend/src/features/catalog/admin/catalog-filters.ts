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

interface BrandListItem {
  name: string;
  slug: string;
  logoPublicId: string | null;
  createdAt: string;
}

export interface BrandListFilters {
  keyword: string;
  logo: string;
  sort: string;
}

const BRAND_SORTERS = {
  'name-asc': (a: BrandListItem, b: BrandListItem) => a.name.localeCompare(b.name, 'vi'),
  'name-desc': (a: BrandListItem, b: BrandListItem) => b.name.localeCompare(a.name, 'vi'),
  newest: (a: BrandListItem, b: BrandListItem) => Date.parse(b.createdAt) - Date.parse(a.createdAt),
  oldest: (a: BrandListItem, b: BrandListItem) => Date.parse(a.createdAt) - Date.parse(b.createdAt),
};

type BrandSort = keyof typeof BRAND_SORTERS;

function hasBrandLogo(brand: BrandListItem): boolean {
  // Mã logo chỉ gồm khoảng trắng không trỏ tới ảnh. Thương hiệu đó thuộc nhóm chưa có logo.
  return brand.logoPublicId !== null && brand.logoPublicId.trim() !== '';
}

export function filterAndSortBrands<T extends BrandListItem>(
  brands: T[],
  filters: BrandListFilters,
): T[] {
  // Áp từ khóa trước để bộ lọc logo và phép sắp xếp chỉ duyệt các thương hiệu đã khớp.
  let matchingBrands = filterBrands(brands, filters.keyword);
  // Chỉ nhận hai mã lọc đã biết. Giá trị lạ trên URL không được làm mất dữ liệu.
  if (filters.logo === 'with-logo') {
    matchingBrands = matchingBrands.filter(hasBrandLogo);
  } else if (filters.logo === 'without-logo') {
    matchingBrands = matchingBrands.filter((brand) => !hasBrandLogo(brand));
  }

  // Giá trị `sort` có thể được nhập tay. Dùng tên tăng dần khi giá trị không thuộc các lựa chọn.
  const sort: BrandSort = Object.hasOwn(BRAND_SORTERS, filters.sort)
    ? (filters.sort as BrandSort)
    : 'name-asc';
  // Sao chép danh sách trước khi sắp xếp để không thay đổi dữ liệu trong cache của React Query.
  return [...matchingBrands].sort(BRAND_SORTERS[sort]);
}
