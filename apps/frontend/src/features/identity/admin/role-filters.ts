import { normalizeForSearch } from '../../../shared/search-text';

// Lọc role theo tên hiển thị và mã role. Từ khóa rỗng trả đúng danh sách vào.
export function filterRoles<T extends { name: string; code: string }>(
  roles: T[],
  keyword: string,
): T[] {
  const normalizedKeyword = normalizeForSearch(keyword);
  if (normalizedKeyword === '') return roles;
  return roles.filter((role) =>
    [role.name, role.code].some((field) => normalizeForSearch(field).includes(normalizedKeyword)),
  );
}
