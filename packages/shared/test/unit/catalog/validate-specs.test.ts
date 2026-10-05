import { describe, expect, it } from 'vitest';
import type { AttributeDefinition } from '../../../src/catalog/attributes';
import { validateSpecs } from '../../../src/catalog/validate-specs';

describe('validateSpecs', () => {
  const definitions: AttributeDefinition[] = [
    {
      key: 'ram_gb',
      label: 'Dung lượng RAM',
      group: 'Bộ nhớ',
      type: 'number',
      unit: 'GB',
      required: true,
      filterable: true,
    },
    {
      key: 'color',
      label: 'Màu sắc',
      group: 'Thiết kế',
      type: 'enum',
      options: ['Đen', 'Bạc', 'Xanh'],
      required: false,
      filterable: true,
    },
    {
      key: 'touchscreen',
      label: 'Màn hình cảm ứng',
      group: 'Màn hình',
      type: 'boolean',
      required: false,
      filterable: true,
    },
    {
      key: 'cpu_model',
      label: 'Vi xử lý',
      group: 'Hiệu năng',
      type: 'text',
      required: true,
      filterable: false,
    },
  ];

  it('chấp nhận bộ thông số đầy đủ và hợp lệ', () => {
    const specs = {
      ram_gb: 16,
      color: 'Bạc',
      touchscreen: false,
      cpu_model: 'Intel Core i7-13650HX',
    };
    const result = validateSpecs(specs, definitions);
    expect(result.valid).toBe(true);
    expect(result.errors).toHaveLength(0);
  });

  it('chấp nhận khi bỏ qua thuộc tính không bắt buộc', () => {
    const specs = {
      ram_gb: 32,
      cpu_model: 'Apple M3 Pro',
    };
    const result = validateSpecs(specs, definitions);
    expect(result.valid).toBe(true);
    expect(result.errors).toHaveLength(0);
  });

  it('báo lỗi khi thiếu thuộc tính bắt buộc và chỉ rõ key thiếu', () => {
    const specs = {
      color: 'Đen',
    };
    const result = validateSpecs(specs, definitions);
    expect(result.valid).toBe(false);

    const errorKeys = result.errors.map((err) => err.key);
    expect(errorKeys).toContain('ram_gb');
    expect(errorKeys).toContain('cpu_model');
  });

  it('báo lỗi khi thuộc tính bắt buộc có giá trị null hoặc undefined', () => {
    const specs = {
      ram_gb: null,
      cpu_model: undefined,
    };
    const result = validateSpecs(specs, definitions);
    expect(result.valid).toBe(false);

    const errorKeys = result.errors.map((err) => err.key);
    expect(errorKeys).toContain('ram_gb');
    expect(errorKeys).toContain('cpu_model');
  });

  it('báo lỗi theo từng key khi sai kiểu number', () => {
    const specsString = {
      ram_gb: '16',
      cpu_model: 'Core i5',
    };
    const resultString = validateSpecs(specsString, definitions);
    expect(resultString.valid).toBe(false);
    expect(resultString.errors.some((err) => err.key === 'ram_gb')).toBe(true);

    const specsNan = {
      ram_gb: Number.NaN,
      cpu_model: 'Core i5',
    };
    const resultNan = validateSpecs(specsNan, definitions);
    expect(resultNan.valid).toBe(false);
    expect(resultNan.errors.some((err) => err.key === 'ram_gb')).toBe(true);
  });

  it('báo lỗi theo từng key khi sai kiểu boolean', () => {
    const specs = {
      ram_gb: 16,
      cpu_model: 'Core i5',
      touchscreen: 'true',
    };
    const result = validateSpecs(specs, definitions);
    expect(result.valid).toBe(false);
    expect(result.errors.some((err) => err.key === 'touchscreen')).toBe(true);
  });

  it('báo lỗi theo từng key khi enum nhận giá trị ngoài options', () => {
    const specs = {
      ram_gb: 16,
      cpu_model: 'Core i5',
      color: 'Đỏ',
    };
    const result = validateSpecs(specs, definitions);
    expect(result.valid).toBe(false);
    expect(result.errors.some((err) => err.key === 'color')).toBe(true);
  });

  it('báo lỗi theo từng key khi text nhận chuỗi rỗng hoặc chỉ có khoảng trắng', () => {
    const specsEmpty = {
      ram_gb: 16,
      cpu_model: '',
    };
    const resultEmpty = validateSpecs(specsEmpty, definitions);
    expect(resultEmpty.valid).toBe(false);
    expect(resultEmpty.errors.some((err) => err.key === 'cpu_model')).toBe(true);

    const specsWhitespace = {
      ram_gb: 16,
      cpu_model: '   ',
    };
    const resultWhitespace = validateSpecs(specsWhitespace, definitions);
    expect(resultWhitespace.valid).toBe(false);
    expect(resultWhitespace.errors.some((err) => err.key === 'cpu_model')).toBe(true);
  });

  it('từ chối khóa không tồn tại trong bộ thuộc tính', () => {
    const specs = {
      ram_gb: 16,
      cpu_model: 'Core i5',
      unknown_field: 'Giá trị lạ',
    };
    const result = validateSpecs(specs, definitions);
    expect(result.valid).toBe(false);
    expect(result.errors.some((err) => err.key === 'unknown_field')).toBe(true);
  });
  it('báo thiếu thuộc tính khi thuộc tính có key constructor bị thiếu thay vì báo sai kiểu', () => {
    const constructorDefinition: AttributeDefinition = {
      key: 'constructor',
      label: 'Nhà sản xuất',
      group: 'Tổng quan',
      type: 'text',
      required: true,
      filterable: false,
    };
    const emptySpecs = {};
    const result = validateSpecs(emptySpecs, [constructorDefinition]);
    expect(result.valid).toBe(false);
    expect(result.errors).toHaveLength(1);
    expect(result.errors[0]?.key).toBe('constructor');
    expect(result.errors[0]?.message).toContain('Thiếu thuộc tính bắt buộc');
  });

  it('từ chối dữ liệu thông số không phải object', () => {
    expect(validateSpecs(null as unknown as Record<string, unknown>, definitions).valid).toBe(
      false,
    );
    expect(validateSpecs([] as unknown as Record<string, unknown>, definitions).valid).toBe(false);
    expect(validateSpecs('chuỗi' as unknown as Record<string, unknown>, definitions).valid).toBe(
      false,
    );
  });
});
