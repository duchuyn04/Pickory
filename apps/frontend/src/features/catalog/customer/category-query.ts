export type ProductSort = 'newest' | 'price_asc' | 'price_desc';

export const DEFAULT_SORT: ProductSort = 'newest';
export const DEFAULT_PAGE_SIZE = 24;
export const MAX_PAGE_SIZE = 48;

const SORT_VALUES: readonly ProductSort[] = ['newest', 'price_asc', 'price_desc'];
const ATTRIBUTE_PARAMETER_PREFIX = 'attr.';

// Trạng thái lọc của trang danh mục. Tên trường khớp tên tham số của `GET /api/products`.
export interface CategoryQuery {
  brands: string[];
  // Giá giữ dạng chữ để ô nhập không mất số 0 đầu hoặc nhảy khi người dùng đang gõ.
  priceMin: string;
  priceMax: string;
  inStock: boolean;
  // Khóa là phần sau `attr.`, ví dụ `ram_gb` hoặc `screen_min`. Mỗi khóa có ít nhất một giá trị.
  attributes: Record<string, string[]>;
  sort: ProductSort;
  page: number;
  pageSize: number;
}

export const EMPTY_CATEGORY_QUERY: CategoryQuery = {
  brands: [],
  priceMin: '',
  priceMax: '',
  inStock: false,
  attributes: {},
  sort: DEFAULT_SORT,
  page: 1,
  pageSize: DEFAULT_PAGE_SIZE,
};

// Tên tham số của cận dưới và cận trên với thuộc tính kiểu số, theo quy ước `_min` và `_max` của API.
export function attributeRangeKey(attributeKey: string, bound: 'min' | 'max'): string {
  return `${attributeKey}_${bound}`;
}

function splitList(rawValue: string): string[] {
  const items = rawValue
    .split(',')
    .map((item) => item.trim())
    .filter((item) => item !== '');
  // Giữ thứ tự gặp đầu tiên và bỏ trùng, để URL sửa tay không tạo bộ lọc lặp.
  return [...new Set(items)];
}

function readPrice(rawValue: string | null): string {
  // Chỉ nhận số nguyên không âm vì API từ chối giá khác dạng này bằng lỗi 400.
  return rawValue !== null && /^\d+$/.test(rawValue) ? rawValue : '';
}

function readPositiveInteger(rawValue: string | null): number | null {
  if (rawValue === null || !/^\d+$/.test(rawValue)) return null;
  const value = Number(rawValue);
  return Number.isSafeInteger(value) && value >= 1 ? value : null;
}

// Đọc query string thành trạng thái lọc. Giá trị hỏng rơi về mặc định để URL sửa tay không làm trắng trang.
export function parseCategoryQuery(params: URLSearchParams): CategoryQuery {
  const attributes: Record<string, string[]> = {};
  for (const [name, rawValue] of params.entries()) {
    if (!name.startsWith(ATTRIBUTE_PARAMETER_PREFIX)) continue;
    const key = name.slice(ATTRIBUTE_PARAMETER_PREFIX.length);
    const values = splitList(rawValue);
    // Khóa rỗng hoặc danh sách rỗng không có nghĩa lọc, nên bỏ qua.
    if (key === '' || values.length === 0) continue;
    attributes[key] = values;
  }

  const rawSort = params.get('sort');
  const sort = SORT_VALUES.find((candidate) => candidate === rawSort) ?? DEFAULT_SORT;
  const requestedPageSize = readPositiveInteger(params.get('pageSize'));
  // Kích thước trang vượt trần của API sẽ bị từ chối, nên rơi về mặc định.
  const pageSize =
    requestedPageSize !== null && requestedPageSize <= MAX_PAGE_SIZE
      ? requestedPageSize
      : DEFAULT_PAGE_SIZE;

  return {
    brands: splitList(params.get('brand') ?? ''),
    priceMin: readPrice(params.get('priceMin')),
    priceMax: readPrice(params.get('priceMax')),
    inStock: params.get('inStock') === 'true',
    attributes,
    sort,
    page: readPositiveInteger(params.get('page')) ?? 1,
    pageSize,
  };
}

// Ghi trạng thái thành query string. Bỏ giá trị mặc định để URL chia sẻ ngắn và ổn định.
export function serializeCategoryQuery(query: CategoryQuery): URLSearchParams {
  const params = new URLSearchParams();
  // Sắp giá trị theo bảng chữ cái để cùng một bộ lọc luôn cho cùng một URL.
  if (query.brands.length > 0) params.set('brand', [...query.brands].sort().join(','));
  if (query.priceMin !== '') params.set('priceMin', query.priceMin);
  if (query.priceMax !== '') params.set('priceMax', query.priceMax);
  if (query.inStock) params.set('inStock', 'true');

  const attributeKeys = Object.keys(query.attributes).sort();
  for (const key of attributeKeys) {
    const values = query.attributes[key] ?? [];
    if (values.length === 0) continue;
    params.set(`${ATTRIBUTE_PARAMETER_PREFIX}${key}`, [...values].sort().join(','));
  }

  if (query.sort !== DEFAULT_SORT) params.set('sort', query.sort);
  if (query.page > 1) params.set('page', String(query.page));
  if (query.pageSize !== DEFAULT_PAGE_SIZE) params.set('pageSize', String(query.pageSize));
  return params;
}

// Dấu phẩy ngăn cách danh sách nên giữ nguyên, để URL dễ đọc và dễ sửa tay.
export function toSearchString(params: URLSearchParams): string {
  return params.toString().replaceAll('%2C', ',');
}

export function buildProductsApiPath(categorySlug: string, query: CategoryQuery): string {
  // Danh mục nằm trong path của trang nên chỉ thêm vào API ở đây, đứng đầu danh sách tham số.
  const params = new URLSearchParams({ category: categorySlug });
  for (const [name, value] of serializeCategoryQuery(query).entries()) {
    params.set(name, value);
  }
  return `/api/products?${toSearchString(params)}`;
}

// Đổi bộ lọc luôn về trang 1, vì trang cũ có thể vượt tổng số trang của kết quả mới.
function withFilterChange(query: CategoryQuery, change: Partial<CategoryQuery>): CategoryQuery {
  return { ...query, ...change, page: 1 };
}

function toggleItem(items: string[], item: string): string[] {
  return items.includes(item) ? items.filter((existing) => existing !== item) : [...items, item];
}

export function toggleBrand(query: CategoryQuery, brandSlug: string): CategoryQuery {
  return withFilterChange(query, { brands: toggleItem(query.brands, brandSlug) });
}

export function setAttributeValues(
  query: CategoryQuery,
  key: string,
  values: string[],
): CategoryQuery {
  const attributes = { ...query.attributes };
  // Khóa không còn giá trị thì xóa hẳn, để URL không giữ tham số rỗng.
  if (values.length === 0) {
    delete attributes[key];
  } else {
    attributes[key] = values;
  }
  return withFilterChange(query, { attributes });
}

export function toggleAttributeValue(
  query: CategoryQuery,
  key: string,
  value: string,
): CategoryQuery {
  return setAttributeValues(query, key, toggleItem(query.attributes[key] ?? [], value));
}

// Đặt cả hai cận trong một lần đổi. Nhờ vậy chỉ có một lần về trang 1 và một lần ghi URL.
export function setAttributeRange(
  query: CategoryQuery,
  key: string,
  minValue: string,
  maxValue: string,
): CategoryQuery {
  const attributes = { ...query.attributes };
  const bounds = [
    { name: attributeRangeKey(key, 'min'), value: minValue.trim() },
    { name: attributeRangeKey(key, 'max'), value: maxValue.trim() },
  ];
  for (const { name, value } of bounds) {
    // Ô trống xóa cận để URL không giữ tham số rỗng.
    if (value === '') {
      delete attributes[name];
    } else {
      attributes[name] = [value];
    }
  }
  return withFilterChange(query, { attributes });
}

export function setPriceRange(
  query: CategoryQuery,
  priceMin: string,
  priceMax: string,
): CategoryQuery {
  return withFilterChange(query, { priceMin: priceMin.trim(), priceMax: priceMax.trim() });
}

export function setInStock(query: CategoryQuery, inStock: boolean): CategoryQuery {
  return withFilterChange(query, { inStock });
}

export function setSort(query: CategoryQuery, sort: ProductSort): CategoryQuery {
  return withFilterChange(query, { sort });
}

export function goToPage(query: CategoryQuery, page: number): CategoryQuery {
  return { ...query, page };
}

// Xóa bộ lọc và giữ cách sắp xếp, kích thước trang. Hai giá trị này là lựa chọn hiển thị, không phải bộ lọc.
export function clearFilters(query: CategoryQuery): CategoryQuery {
  return { ...EMPTY_CATEGORY_QUERY, sort: query.sort, pageSize: query.pageSize };
}

// Mỗi giá trị đã chọn và mỗi cận giá tính là một bộ lọc.
export function countActiveFilters(query: CategoryQuery): number {
  const attributeCount = Object.values(query.attributes).reduce(
    (total, values) => total + values.length,
    0,
  );
  return (
    query.brands.length +
    attributeCount +
    (query.priceMin !== '' ? 1 : 0) +
    (query.priceMax !== '' ? 1 : 0) +
    (query.inStock ? 1 : 0)
  );
}
