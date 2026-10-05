import { describe, expect, it } from 'vitest';
import {
  buildVariantCombinations,
  MAX_VARIANT_COMBINATIONS,
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
  it('từ chối khi tên trục tùy chọn bị rỗng hoặc trùng lặp', () => {
    const emptyNameOptions: ProductOption[] = [{ name: '   ', values: ['A', 'B'] }];
    expect(() => buildVariantCombinations(emptyNameOptions)).toThrow(
      /tên trục tùy chọn không được để trống/i,
    );

    const duplicateNameOptions: ProductOption[] = [
      { name: 'Màu sắc', values: ['Đen'] },
      { name: ' Màu sắc ', values: ['Trắng'] },
    ];
    expect(() => buildVariantCombinations(duplicateNameOptions)).toThrow(/trùng lặp/i);
  });

  it('từ chối khi giá trị tùy chọn trong trục bị rỗng hoặc trùng lặp', () => {
    const emptyValueOptions: ProductOption[] = [{ name: 'Màu', values: ['Đen', '   '] }];
    expect(() => buildVariantCombinations(emptyValueOptions)).toThrow(/không được để trống/i);

    const duplicateValueOptions: ProductOption[] = [{ name: 'Màu', values: ['Đen', ' Đen '] }];
    expect(() => buildVariantCombinations(duplicateValueOptions)).toThrow(/trùng lặp/i);
  });

  it('từ chối khi tổng số tổ hợp vượt quá ngưỡng tối đa 100', () => {
    expect(MAX_VARIANT_COMBINATIONS).toBe(100);

    const largeOptions: ProductOption[] = [
      { name: 'Trục 1', values: Array.from({ length: 11 }, (_, i) => `Val1_${i}`) },
      { name: 'Trục 2', values: Array.from({ length: 10 }, (_, i) => `Val2_${i}`) },
    ];
    expect(() => buildVariantCombinations(largeOptions)).toThrow(/vượt quá giới hạn tối đa 100/i);
  });
});
