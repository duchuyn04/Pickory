// Tạo slug chuẩn SEO từ tên sản phẩm tiếng Việt.
export function toSlug(name: string): string {
  // Chuyển toàn bộ chuỗi sang chữ thường để đồng nhất đường dẫn URL.
  const lower = name.toLowerCase();

  // Đổi chữ đ sang d trước khi tách dấu Unicode vì NFD không tách chữ đ.
  const replacedD = lower.replace(/đ/g, 'd');

  // Phân rã ký tự Unicode để bóc tách các dấu thanh tiếng Việt.
  const normalized = replacedD.normalize('NFD');

  // Xóa toàn bộ ký tự dấu kết hợp Unicode sau khi phân rã.
  const withoutAccents = normalized.replace(/[\u0300-\u036f]/g, '');

  // Thay thế toàn bộ ký tự không phải chữ cái và số bằng dấu gạch ngang.
  const withHyphens = withoutAccents.replace(/[^a-z0-9]+/g, '-');

  // Cắt bỏ các dấu gạch ngang thừa ở đầu và cuối chuỗi.
  return withHyphens.replace(/^-+|-+$/g, '');
}
