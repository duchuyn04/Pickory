// Lỗi mang câu tiếng Việt viết cho người dùng đọc. Giao diện chỉ hiện `message` của loại lỗi này.
export class UserFacingError extends Error {}

/**
 * Lấy câu lỗi để hiện trên giao diện.
 * Lỗi JS và lỗi thư viện mang chữ kỹ thuật tiếng Anh, nên hàm trả `fallbackMessage` cho các lỗi đó.
 */
export function getErrorMessage(error: unknown, fallbackMessage: string): string {
  // Chỉ tin câu của `UserFacingError`, vì `TypeError` hay lỗi Zod có thể lộ tên biến và cấu trúc dữ liệu.
  if (error instanceof UserFacingError && error.message) return error.message;
  return fallbackMessage;
}
