import { normalizeForSearch } from '../../shared/search-text';
import type { AddressOption } from './address-api';

// Lọc theo kiểu "chứa". Danh sách vào đã sắp theo tên tiếng Việt từ backend, nên giữ nguyên thứ tự.
export function filterAddressOptions(options: AddressOption[], query: string): AddressOption[] {
  const normalizedQuery = normalizeForSearch(query);
  // Chữ rỗng nghĩa là chưa lọc. Trả cả danh sách để khách duyệt được bằng mũi tên.
  if (normalizedQuery === '') return options;
  return options.filter((option) => normalizeForSearch(option.name).includes(normalizedQuery));
}
