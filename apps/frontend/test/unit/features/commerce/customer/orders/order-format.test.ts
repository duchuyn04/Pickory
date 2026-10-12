import { describe, expect, it } from 'vitest';
import { formatDateTime } from '../../../../../../src/features/commerce/customer/orders/order-format';

describe('formatDateTime', () => {
  it('định dạng ngày hợp lệ theo tiếng Việt', () => {
    expect(formatDateTime('2026-10-12T10:30:00')).toBe('10:30 12/10/2026');
  });
  it('giữ chuỗi gốc khi ngày không hợp lệ', () => {
    expect(formatDateTime('sai')).toBe('sai');
  });
});
