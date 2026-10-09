import { describe, expect, it } from 'vitest';
import type { ProductSpecEntry } from '../../../../../src/features/catalog/customer/catalog-api';
import {
  formatSpecValue,
  groupSpecs,
} from '../../../../../src/features/catalog/customer/spec-format';

function spec(overrides: Partial<ProductSpecEntry>): ProductSpecEntry {
  return {
    key: 'ram',
    label: 'RAM',
    group: 'Cấu hình',
    type: 'enum',
    unit: null,
    value: '16',
    ...overrides,
  };
}

describe('formatSpecValue', () => {
  it('hiện Có hoặc Không cho giá trị boolean', () => {
    expect(formatSpecValue(spec({ type: 'boolean', value: true }))).toBe('Có');
    expect(formatSpecValue(spec({ type: 'boolean', value: false }))).toBe('Không');
  });

  it('kèm đơn vị cho giá trị số và enum', () => {
    expect(formatSpecValue(spec({ type: 'enum', value: '16', unit: 'GB' }))).toBe('16 GB');
    expect(formatSpecValue(spec({ type: 'number', value: 15.6, unit: 'inch' }))).toBe('15,6 inch');
  });

  it('không kèm đơn vị khi thông số không có đơn vị', () => {
    expect(formatSpecValue(spec({ type: 'number', value: 2, unit: null }))).toBe('2');
    expect(formatSpecValue(spec({ type: 'text', value: 'Nhôm', unit: null }))).toBe('Nhôm');
  });
});

describe('groupSpecs', () => {
  it('giữ thứ tự nhóm và thứ tự dòng theo lần xuất hiện đầu tiên', () => {
    const groups = groupSpecs([
      spec({ key: 'a', label: 'A', group: 'Hiển thị' }),
      spec({ key: 'b', label: 'B', group: 'Cấu hình' }),
      spec({ key: 'c', label: 'C', group: 'Hiển thị' }),
    ]);
    expect(groups.map((group) => group.name)).toEqual(['Hiển thị', 'Cấu hình']);
    expect(groups[0]?.rows.map((row) => row.label)).toEqual(['A', 'C']);
  });

  it('bỏ dòng không có giá trị và ẩn nhóm không còn dòng nào', () => {
    const groups = groupSpecs([
      spec({ key: 'a', group: 'Hiển thị', value: null }),
      spec({ key: 'b', group: 'Cấu hình', value: '8' }),
    ]);
    expect(groups.map((group) => group.name)).toEqual(['Cấu hình']);
  });

  it('giữ giá trị false vì Không là thông tin hợp lệ', () => {
    const groups = groupSpecs([spec({ type: 'boolean', value: false })]);
    expect(groups[0]?.rows[0]?.value).toBe('Không');
  });
});
