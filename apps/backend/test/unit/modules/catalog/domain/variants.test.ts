import { describe, expect, it } from 'vitest';
import {
  buildVariantCombinations,
  type ProductOption,
} from '../../../../../src/modules/catalog/domain/variants';

describe('buildVariantCombinations', () => {
  it('sinh đúng 4 tổ hợp cho 2 trục tùy chọn', () => {
    const options: ProductOption[] = [
      { name: 'Dung lượng', values: ['128GB', '256GB'] },
      { name: 'Màu', values: ['Đen', 'Trắng'] },
    ];

    const combinations = buildVariantCombinations(options);
    expect(combinations).toHaveLength(4);
    expect(combinations).toEqual([
      { 'Dung lượng': '128GB', Màu: 'Đen' },
      { 'Dung lượng': '128GB', Màu: 'Trắng' },
      { 'Dung lượng': '256GB', Màu: 'Đen' },
      { 'Dung lượng': '256GB', Màu: 'Trắng' },
    ]);
  });

  it('sinh đúng 1 biến thể mặc định khi có 0 trục tùy chọn', () => {
    const combinations = buildVariantCombinations([]);
    expect(combinations).toHaveLength(1);
    expect(combinations).toEqual([{}]);
  });

  it('sinh đúng số tổ hợp cho 1 trục tùy chọn', () => {
    const options: ProductOption[] = [{ name: 'Màu', values: ['Đen', 'Trắng', 'Xanh'] }];
    const combinations = buildVariantCombinations(options);
    expect(combinations).toHaveLength(3);
    expect(combinations).toEqual([{ Màu: 'Đen' }, { Màu: 'Trắng' }, { Màu: 'Xanh' }]);
  });

  it('sinh đúng 8 tổ hợp cho 3 trục tùy chọn', () => {
    const options: ProductOption[] = [
      { name: 'RAM', values: ['8GB', '16GB'] },
      { name: 'Ổ cứng', values: ['256GB', '512GB'] },
      { name: 'Màu', values: ['Đen', 'Bạc'] },
    ];
    const combinations = buildVariantCombinations(options);
    expect(combinations).toHaveLength(8);
  });

  it('từ chối và ném lỗi khi có từ 4 trục tùy chọn trở lên', () => {
    const options: ProductOption[] = [
      { name: 'Trục 1', values: ['A', 'B'] },
      { name: 'Trục 2', values: ['C', 'D'] },
      { name: 'Trục 3', values: ['E', 'F'] },
      { name: 'Trục 4', values: ['G', 'H'] },
    ];
    expect(() => buildVariantCombinations(options)).toThrow(/tối đa 3 trục/i);
  });
});
