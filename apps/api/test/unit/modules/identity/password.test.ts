import { describe, expect, it } from 'vitest';
import { hashPassword, verifyPassword } from '../../../../src/modules/identity/password';

describe('ID-03 password hashing', () => {
  it('băm mật khẩu bằng Argon2id và xác thực thành công', async () => {
    const raw = 'SuperSecret123!';
    const hashed = await hashPassword(raw);

    // Chuỗi băm theo chuẩn Argon2id
    expect(hashed).toMatch(/^\$argon2id\$/);

    const isMatch = await verifyPassword(hashed, raw);
    expect(isMatch).toBe(true);

    const isWrong = await verifyPassword(hashed, 'WrongPassword!');
    expect(isWrong).toBe(false);
  });
});
