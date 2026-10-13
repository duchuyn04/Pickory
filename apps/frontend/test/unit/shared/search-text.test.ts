import { describe, expect, it } from 'vitest';
import { normalizeForSearch } from '../../../src/shared/search-text';

describe('normalizeForSearch dùng chung', () => {
  it('bỏ dấu và hạ chữ thường để so sánh được', () => {
    expect(normalizeForSearch('Điện Thoại')).toBe('dien thoai');
  });

  it('đổi chữ đ và Đ thành d vì NFD không tách được dấu của hai chữ này', () => {
    expect(normalizeForSearch('đường')).toBe('duong');
    expect(normalizeForSearch('Đà Nẵng')).toBe('da nang');
  });

  it('gộp khoảng trắng thừa và bỏ khoảng trắng hai đầu', () => {
    expect(normalizeForSearch('  ha    noi  ')).toBe('ha noi');
  });
});
