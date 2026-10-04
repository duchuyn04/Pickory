import { hash, verify } from '@node-rs/argon2';

// Băm mật khẩu để lưu DB.
export async function hashPassword(plainText: string): Promise<string> {
  // Argon2id (mặc định của thư viện) tốn cả CPU lẫn RAM, làm tấn công dò mật khẩu bằng GPU rất đắt.
  // Salt do thư viện tự sinh và nhúng vào chuỗi hash, nên không cần lưu riêng.
  return hash(plainText);
}

// Kiểm tra mật khẩu nhập vào với hash trong DB.
export async function verifyPassword(hashString: string, plainText: string): Promise<boolean> {
  try {
    // Thư viện đọc tham số và salt từ chính chuỗi hash để băm lại và so sánh.
    return await verify(hashString, plainText);
  } catch {
    // Hash hỏng hoặc sai định dạng thì coi như mật khẩu sai, không để lỗi lộ ra làm lộ thông tin nội bộ.
    return false;
  }
}
