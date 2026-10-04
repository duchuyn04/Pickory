import { hash, verify } from '@node-rs/argon2';

/**
 * Băm mật khẩu người dùng bằng thuật toán Argon2id nhằm chống tấn công vét cạn và dò bằng GPU.
 */
export async function hashPassword(plainText: string): Promise<string> {
  return hash(plainText);
}

/**
 * Xác minh mật khẩu nhập vào có khớp với chuỗi băm Argon2id lưu trong cơ sở dữ liệu.
 */
export async function verifyPassword(hashString: string, plainText: string): Promise<boolean> {
  try {
    return await verify(hashString, plainText);
  } catch {
    // Trả về false thay vì để lộ exception nếu chuỗi hash đầu vào sai định dạng hoặc bị lỗi giải mã.
    return false;
  }
}
