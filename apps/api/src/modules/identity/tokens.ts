import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';

/**
 * Sinh chuỗi ngẫu nhiên 32 byte dạng hex làm token phiên hoặc email token.
 */
export function generateToken(): string {
  return randomBytes(32).toString('hex');
}

/**
 * Băm token bằng SHA-256 để lưu vào database (không bao giờ lưu token gốc).
 */
export function hashToken(token: string): string {
  return createHash('sha256').update(token).digest('hex');
}

/**
 * So sánh hai chuỗi hash bằng hàm constant-time chống tấn công timing.
 */
export function safeEqual(a: string, b: string): boolean {
  const bufA = Buffer.from(a);
  const bufB = Buffer.from(b);
  if (bufA.length !== bufB.length) {
    return false;
  }
  return timingSafeEqual(bufA, bufB);
}
