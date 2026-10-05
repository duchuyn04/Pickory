import { describe, expect, it } from 'vitest';
import {
  resolveCategoryAttributes,
  validateAttributeDefinition,
  validateAttributeDefinitions,
  type AttributeDefinition,
} from '../../../src/catalog/attributes';

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
