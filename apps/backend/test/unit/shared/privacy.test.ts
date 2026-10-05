import { describe, expect, it } from 'vitest';
import { redactPII } from '../../../src/shared/privacy';

describe('redactPII', () => {
  it.each([
    '0912 345 678',
    '+84 912345678',
    '091.234.5678',
    'a@b.vn',
    '4111 1111 1111 1111',
    '12345678901234',
    'STK: 12345678',
    'Số tài khoản 1234 5678 9012',
  ])('ẩn thông tin nhạy cảm %s', (value) => {
    expect(redactPII(`Liên hệ ${value}.`)).not.toContain(value);
    expect(redactPII(value)).toContain('[đã ẩn]');
  });
  it.each(['25.990.000', '25990000đ', '5 triệu', 'RTX 4070', 'SM-S918B', 'Giá 25 990 000 VND'])(
    'giữ nguyên giá và model %s',
    (value) => {
      expect(redactPII(value)).toBe(value);
    },
  );
  it('ẩn nhiều giá trị và chạy lại không thay đổi kết quả', () => {
    const value = 'Gọi 0912 345 678 hoặc a@b.vn, giá 25.990.000đ.';
    const redacted = 'Gọi [đã ẩn] hoặc [đã ẩn], giá 25.990.000đ.';
    expect(redactPII(value)).toBe(redacted);
    expect(redactPII(redacted)).toBe(redacted);
  });
});
