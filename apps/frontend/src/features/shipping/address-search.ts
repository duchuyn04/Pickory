import type { AddressOption } from './address-api';

// Đưa chữ về dạng so sánh được: thường, không dấu, một khoảng trắng giữa các từ.
export function normalizeForSearch(text: string): string {
  return (
    text
      .toLowerCase()
      // NFD tách chữ có dấu thành chữ gốc cộng dấu rời.
      .normalize('NFD')
      // Xóa khoảng `\u0300-\u036f` (nhóm "Combining Diacritical Marks") để còn chữ gốc không dấu.
      .replace(/[\u0300-\u036f]/g, '')
      // Chữ `đ` là một chữ riêng, không phải `d` cộng dấu, nên NFD không tách được. Phải đổi riêng.
      .replace(/đ/g, 'd')
      // Khách gõ thừa khoảng trắng vẫn tìm được. Nhiều khoảng trắng liền nhau thành một.
      .replace(/\s+/g, ' ')
      .trim()
  );
}

// Lọc theo kiểu "chứa". Danh sách vào đã sắp theo tên tiếng Việt từ backend, nên giữ nguyên thứ tự.
export function filterAddressOptions(options: AddressOption[], query: string): AddressOption[] {
  const normalizedQuery = normalizeForSearch(query);
  // Chữ rỗng nghĩa là chưa lọc. Trả cả danh sách để khách duyệt được bằng mũi tên.
  if (normalizedQuery === '') return options;
  return options.filter((option) => normalizeForSearch(option.name).includes(normalizedQuery));
}
