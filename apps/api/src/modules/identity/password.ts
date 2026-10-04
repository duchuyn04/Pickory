import { hash, verify } from '@node-rs/argon2';

/**
 * Băm mật khẩu bằng Argon2id theo chuẩn OWASP và spec/SPEC-identity.md.
 */
export async function hashPassword(plainText: string): Promise<string> {
  return hash(plainText);
}

/**
 * Kiểm tra mật khẩu đối chiếu với chuỗi hash Argon2id.
 */
export async function verifyPassword(hashString: string, plainText: string): Promise<boolean> {
  try {
    return await verify(hashString, plainText);
  } catch {
    return false;
  }
}
