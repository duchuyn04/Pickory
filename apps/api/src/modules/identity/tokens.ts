import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';

// Sinh token ngẫu nhiên dùng cho phiên đăng nhập và link email.
export function generateToken(): string {
  // randomBytes lấy từ bộ sinh ngẫu nhiên của hệ điều hành nên không đoán trước được; 32 byte cho 256 bit entropy.
  return randomBytes(32).toString('hex');
}

// Băm token trước khi lưu DB.
export function hashToken(token: string): string {
  // DB chỉ giữ hash, nên lộ DB cũng không dùng được token gốc để đăng nhập.
  // SHA-256 đủ dùng vì token đã ngẫu nhiên 256 bit, không cần băm chậm như mật khẩu.
  return createHash('sha256').update(token).digest('hex');
}

// So sánh hai chuỗi mà thời gian chạy không phụ thuộc vào vị trí ký tự khác nhau.
export function safeEqual(a: string, b: string): boolean {
  // Đổi sang Buffer vì timingSafeEqual chỉ nhận dữ liệu nhị phân.
  const bufA = Buffer.from(a);
  const bufB = Buffer.from(b);
  // timingSafeEqual ném lỗi nếu hai buffer khác độ dài, nên phải kiểm tra trước.
  if (bufA.length !== bufB.length) {
    return false;
  }
  // So sánh toàn bộ byte, không dừng sớm ở byte đầu tiên khác, để kẻ tấn công không đo thời gian mà đoán dần từng ký tự.
  return timingSafeEqual(bufA, bufB);
}
