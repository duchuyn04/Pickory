import { describe, expect, it } from 'vitest';
import {
  resolveCategoryAttributes,
  validateAttributeDefinition,
  validateAttributeDefinitions,
  type AttributeDefinition,
} from '../../../../src/shared/catalog/attributes';

describe('catalog attributes', () => {
  const baseRamAttribute: AttributeDefinition = {
    key: 'ram_gb',
    label: 'Dung lượng RAM',
    group: 'Bộ nhớ',
    type: 'number',
    unit: 'GB',
    required: true,
    filterable: true,
  };

  const baseColorAttribute: AttributeDefinition = {
    key: 'color',
    label: 'Màu sắc',
    group: 'Thiết kế',
    type: 'enum',
    options: ['Đen', 'Bạc', 'Xanh'],
    required: false,
    filterable: true,
  };

  describe('validateAttributeDefinition', () => {
    it('chấp nhận định nghĩa thuộc tính hợp lệ', () => {
      const result = validateAttributeDefinition(baseRamAttribute);
      expect(result.valid).toBe(true);
      expect(result.errors).toHaveLength(0);
    });

    it('từ chối key sai định dạng mẫu', () => {
      const invalidKeys = ['Ram_gb', '1ram', 'ram-gb', 'ram gb', '', '_ram'];
      for (const invalidKey of invalidKeys) {
        const result = validateAttributeDefinition({
          ...baseRamAttribute,
          key: invalidKey,
        });
        expect(result.valid).toBe(false);
        expect(result.errors.some((err) => err.key === invalidKey)).toBe(true);
      }
    });

    it('từ chối enum khi không có danh sách options hoặc danh sách rỗng', () => {
      const enumWithoutOptions: AttributeDefinition = {
        key: 'screen_type',
        label: 'Loại màn hình',
        group: 'Màn hình',
        type: 'enum',
        required: true,
        filterable: true,
      };
      const resultWithoutOptions = validateAttributeDefinition(enumWithoutOptions);
      expect(resultWithoutOptions.valid).toBe(false);
      expect(resultWithoutOptions.errors[0]?.key).toBe('screen_type');

      const enumWithEmptyOptions: AttributeDefinition = {
        ...enumWithoutOptions,
        options: [],
      };
      const resultWithEmptyOptions = validateAttributeDefinition(enumWithEmptyOptions);
      expect(resultWithEmptyOptions.valid).toBe(false);
      expect(resultWithEmptyOptions.errors[0]?.key).toBe('screen_type');
    });

    it('chấp nhận thuộc tính khác enum mà không có options', () => {
      const textAttribute: AttributeDefinition = {
        key: 'cpu_model',
        label: 'Vi xử lý',
        group: 'Hiệu năng',
        type: 'text',
        required: true,
        filterable: false,
      };
      const result = validateAttributeDefinition(textAttribute);
      expect(result.valid).toBe(true);
    });
    it('từ chối type nằm ngoài 4 giá trị cho phép', () => {
      const invalidTypeAttribute = {
        ...baseRamAttribute,
        type: 'date' as unknown as AttributeDefinition['type'],
      };
      const result = validateAttributeDefinition(invalidTypeAttribute);
      expect(result.valid).toBe(false);
      expect(result.errors.some((err) => err.key === 'ram_gb')).toBe(true);
    });

    it('từ chối enum khi options chứa giá trị rỗng sau trim hoặc trùng lặp', () => {
      const enumWithEmptyOption: AttributeDefinition = {
        ...baseColorAttribute,
        options: ['Đen', '   ', 'Bạc'],
      };
      const resultEmpty = validateAttributeDefinition(enumWithEmptyOption);
      expect(resultEmpty.valid).toBe(false);
      expect(resultEmpty.errors.some((err) => err.key === 'color')).toBe(true);

      const enumWithDuplicateOptions: AttributeDefinition = {
        ...baseColorAttribute,
        options: ['Đen', 'Bạc', ' Đen '],
      };
      const resultDuplicate = validateAttributeDefinition(enumWithDuplicateOptions);
      expect(resultDuplicate.valid).toBe(false);
      expect(resultDuplicate.errors.some((err) => err.key === 'color')).toBe(true);
    });
  });

  describe('field và index của lỗi', () => {
    it('mỗi loại lỗi của một dòng có đúng field và index', () => {
      const brokenAttributes: AttributeDefinition[] = [
        baseRamAttribute,
        {
          key: 'Sai',
          label: ' ',
          group: '',
          type: 'date' as unknown as AttributeDefinition['type'],
          required: false,
          filterable: false,
        },
        { ...baseColorAttribute, options: [] },
      ];

      const result = validateAttributeDefinitions(brokenAttributes);
      const fieldsWithIndex = result.errors.map((err) => [err.index, err.field]);

      // Form gắn lỗi vào ô theo cặp index và field, nên mỗi lỗi phải mang đủ hai trường.
      expect(fieldsWithIndex).toEqual([
        [1, 'key'],
        [1, 'label'],
        [1, 'group'],
        [1, 'type'],
        [2, 'options'],
      ]);
    });

    it('lỗi trùng khóa mang field key và index của dòng lặp', () => {
      const result = validateAttributeDefinitions([baseRamAttribute, baseRamAttribute]);
      expect(result.errors).toHaveLength(1);
      expect(result.errors[0]?.field).toBe('key');
      expect(result.errors[0]?.index).toBe(1);
    });

    it('validateAttributeDefinition gọi riêng không có index', () => {
      const result = validateAttributeDefinition({ ...baseRamAttribute, label: '' });
      expect(result.errors[0]?.field).toBe('label');
      expect(result.errors[0]?.index).toBeUndefined();
    });
  });

  describe('dừng ở lỗi option đầu tiên như backend', () => {
    it('nhiều option rỗng chỉ sinh một lỗi', () => {
      const result = validateAttributeDefinition({ ...baseColorAttribute, options: ['', ''] });
      // `AttributeDefinitionRules.cs` trả về ngay ở lỗi option đầu tiên. Form phải cùng luật.
      expect(result.errors).toHaveLength(1);
      expect(result.errors[0]?.message).toContain('không được để trống');
    });

    it('nhiều option trùng chỉ sinh một lỗi', () => {
      const result = validateAttributeDefinition({
        ...baseColorAttribute,
        options: ['Đen', 'Đen', 'Bạc', 'Bạc'],
      });
      expect(result.errors).toHaveLength(1);
      expect(result.errors[0]?.message).toContain('"Đen"');
    });

    it('hai khóa rỗng sinh thêm lỗi trùng khóa như backend', () => {
      const emptyKeyAttribute = { ...baseRamAttribute, key: '' };
      const result = validateAttributeDefinitions([emptyKeyAttribute, emptyKeyAttribute]);
      const keyErrorsOfSecondRow = result.errors.filter(
        (err) => err.index === 1 && err.field === 'key',
      );
      expect(keyErrorsOfSecondRow).toHaveLength(2);
    });
  });

  describe('validateAttributeDefinitions', () => {
    it('từ chối danh sách chứa trùng khóa', () => {
      const result = validateAttributeDefinitions([
        baseRamAttribute,
        { ...baseRamAttribute, label: 'RAM khác' },
      ]);
      expect(result.valid).toBe(false);
      expect(result.errors.some((err) => err.key === 'ram_gb')).toBe(true);
    });
  });

  describe('resolveCategoryAttributes', () => {
    it('gộp thuộc tính cha và thuộc tính con theo đúng thứ tự', () => {
      const parentAttributes: AttributeDefinition[] = [baseRamAttribute];
      const childAttributes: AttributeDefinition[] = [baseColorAttribute];

      const resolved = resolveCategoryAttributes(parentAttributes, childAttributes);
      expect(resolved).toHaveLength(2);
      expect(resolved[0]?.key).toBe('ram_gb');
      expect(resolved[1]?.key).toBe('color');
    });

    it('trả danh sách thuộc tính con khi danh mục cha rỗng hoặc không có', () => {
      const childAttributes: AttributeDefinition[] = [baseColorAttribute];
      expect(resolveCategoryAttributes(null, childAttributes)).toEqual(childAttributes);
      expect(resolveCategoryAttributes(undefined, childAttributes)).toEqual(childAttributes);
      expect(resolveCategoryAttributes([], childAttributes)).toEqual(childAttributes);
    });

    it('ném lỗi định nghĩa khi trùng key giữa danh mục cha và danh mục con', () => {
      const parentAttributes: AttributeDefinition[] = [baseRamAttribute];
      const childAttributes: AttributeDefinition[] = [{ ...baseRamAttribute, label: 'RAM con' }];

      expect(() => resolveCategoryAttributes(parentAttributes, childAttributes)).toThrow(/trùng/i);
    });
  });
});
