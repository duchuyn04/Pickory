import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';

/**
 * Sinh chuỗi ngẫu nhiên an toàn mật mã 32 byte dạng hex làm token phiên hoặc email token.
 */
export function generateToken(): string {
  return randomBytes(32).toString('hex');
}

/**
 * Băm token bằng SHA-256 để lưu vào cơ sở dữ liệu nhằm phòng ngừa rò rỉ token gốc khi lộ dữ liệu.
 */
export function hashToken(token: string): string {
  return createHash('sha256').update(token).digest('hex');
}

/**
 * So sánh hai chuỗi bằng thuật toán đẳng thời (constant-time) nhằm triệt tiêu nguy cơ lộ dữ liệu qua tấn công timing.
 */
export function safeEqual(a: string, b: string): boolean {
  const bufA = Buffer.from(a);
  const bufB = Buffer.from(b);
  // Kiểm tra độ dài trước vì timingSafeEqual bắt buộc hai Buffer phải có cùng kích thước byte.
  if (bufA.length !== bufB.length) {
    return false;
  }
  return timingSafeEqual(bufA, bufB);
}
