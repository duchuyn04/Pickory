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
