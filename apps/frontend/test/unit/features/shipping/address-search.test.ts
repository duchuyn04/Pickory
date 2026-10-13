import { describe, expect, it } from 'vitest';
import { filterAddressOptions } from '../../../../src/features/shipping/address-search';
import { normalizeForSearch } from '../../../../src/shared/search-text';

const options = [
  { code: '1', name: 'Cần Thơ' },
  { code: '2', name: 'Đà Nẵng' },
  { code: '3', name: 'Hà Nội' },
  { code: '4', name: 'Hồ Chí Minh' },
  { code: '5', name: 'Phường Đa Kao' },
];

describe('normalizeForSearch', () => {
  it('bỏ dấu thanh và dấu mũ của tiếng Việt', () => {
    expect(normalizeForSearch('Hà Nội')).toBe('ha noi');
    expect(normalizeForSearch('Hồ Chí Minh')).toBe('ho chi minh');
  });

  it('đổi chữ đ và Đ thành d vì NFD không tách được dấu của hai chữ này', () => {
    expect(normalizeForSearch('Đà Nẵng')).toBe('da nang');
    expect(normalizeForSearch('đường')).toBe('duong');
  });

  it('không phân biệt hoa thường', () => {
    expect(normalizeForSearch('HÀ NỘI')).toBe('ha noi');
  });

  it('bỏ khoảng trắng thừa ở đầu, cuối và giữa', () => {
    expect(normalizeForSearch('  ha    noi  ')).toBe('ha noi');
  });

  it('chuỗi chỉ có khoảng trắng thành chuỗi rỗng', () => {
    expect(normalizeForSearch('   ')).toBe('');
  });
});

describe('filterAddressOptions', () => {
  it('gõ "ha noi" thấy "Hà Nội"', () => {
    expect(filterAddressOptions(options, 'ha noi').map((option) => option.name)).toEqual([
      'Hà Nội',
    ]);
  });

  it('gõ "da nang" thấy "Đà Nẵng"', () => {
    expect(filterAddressOptions(options, 'da nang').map((option) => option.name)).toEqual([
      'Đà Nẵng',
    ]);
  });

  it('lọc theo kiểu chứa, không chỉ theo đầu chuỗi', () => {
    expect(filterAddressOptions(options, 'kao').map((option) => option.name)).toEqual([
      'Phường Đa Kao',
    ]);
  });

  it('chữ gõ có dấu vẫn khớp tên có dấu', () => {
    expect(filterAddressOptions(options, 'Hà Nội').map((option) => option.name)).toEqual([
      'Hà Nội',
    ]);
  });

  it('chữ gõ có khoảng trắng thừa vẫn khớp', () => {
    expect(filterAddressOptions(options, '  ha   noi ').map((option) => option.name)).toEqual([
      'Hà Nội',
    ]);
  });

  it('chữ rỗng trả toàn bộ danh sách theo thứ tự gốc', () => {
    expect(filterAddressOptions(options, '   ')).toEqual(options);
  });

  it('không khớp dòng nào thì trả danh sách rỗng', () => {
    expect(filterAddressOptions(options, 'xyz')).toEqual([]);
  });

  it('giữ nguyên thứ tự đã sắp của backend', () => {
    expect(filterAddressOptions(options, 'h').map((option) => option.name)).toEqual([
      'Cần Thơ',
      'Hà Nội',
      'Hồ Chí Minh',
      'Phường Đa Kao',
    ]);
  });
});
