import { describe, expect, it } from 'vitest';
import {
  generateToken,
  hashToken,
  safeEqual,
} from '../../../../../src/modules/identity/security/tokens';

describe('ID-03 tokens and hash', () => {
  it('sinh token 32 byte hex và hash SHA-256 một chiều', () => {
    const token = generateToken();
    expect(token).toHaveLength(64); // 32 bytes = 64 hex chars

    const hash1 = hashToken(token);
    expect(hash1).toHaveLength(64); // SHA-256 = 32 bytes hex
    expect(hash1).not.toBe(token);

    // Hash tính nhất quán
    const hash2 = hashToken(token);
    expect(hash1).toBe(hash2);
  });

  it('so sánh constant-time bằng safeEqual', () => {
    const token = 'abcdef123456';
    expect(safeEqual(token, token)).toBe(true);
    expect(safeEqual(token, 'abcdef123457')).toBe(false);
    expect(safeEqual(token, 'short')).toBe(false);
  });
});
