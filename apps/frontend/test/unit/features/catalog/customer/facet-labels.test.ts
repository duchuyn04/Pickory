import { describe, expect, it } from 'vitest';
import type {
  AttributeFacet,
  BrandFacet,
} from '../../../../../src/features/catalog/customer/catalog-api';
import {
  attributeLegend,
  buildAttributeOptions,
  buildBrandOptions,
  formatOptionLabel,
} from '../../../../../src/features/catalog/customer/facet-labels';

const ramFacet: AttributeFacet = {
  key: 'ram_gb',
  label: 'RAM',
  type: 'enum',
  unit: 'GB',
  values: [
    { value: '8', count: 5 },
    { value: '16', count: 12 },
  ],
};

describe('nhãn có số đếm', () => {
  it('ghép giá trị, đơn vị và số đếm thành "16 GB (12)"', () => {
    const options = buildAttributeOptions(ramFacet, []);

    expect(options.map(formatOptionLabel)).toEqual(['8 GB (5)', '16 GB (12)']);
  });

  it('không thêm đơn vị khi facet không có đơn vị', () => {
    const facet: AttributeFacet = {
      key: 'cpu',
      label: 'CPU',
      type: 'text',
      values: [{ value: 'Core i7', count: 3 }],
    };

    expect(buildAttributeOptions(facet, []).map(formatOptionLabel)).toEqual(['Core i7 (3)']);
  });

  it('hiện "Có" và "Không" cho thuộc tính boolean', () => {
    const facet: AttributeFacet = {
      key: 'touch',
      label: 'Cảm ứng',
      type: 'boolean',
      values: [
        { value: false, count: 4 },
        { value: true, count: 2 },
      ],
    };

    const options = buildAttributeOptions(facet, []);

    expect(options.map((option) => option.value)).toEqual(['false', 'true']);
    expect(options.map(formatOptionLabel)).toEqual(['Không (4)', 'Có (2)']);
  });

  it('nhãn thương hiệu có số đếm', () => {
    const brands: BrandFacet[] = [{ slug: 'asus', name: 'ASUS', count: 7 }];

    expect(buildBrandOptions(brands, []).map(formatOptionLabel)).toEqual(['ASUS (7)']);
  });
});

describe('giá trị đang chọn không còn trong facets', () => {
  it('thuộc tính vẫn hiện giá trị đã chọn với số đếm 0, đứng sau các giá trị khác', () => {
    const options = buildAttributeOptions(ramFacet, ['32']);

    expect(options.map(formatOptionLabel)).toEqual(['8 GB (5)', '16 GB (12)', '32 GB (0)']);
  });

  it('thương hiệu vẫn hiện giá trị đã chọn, dùng slug làm tên', () => {
    const options = buildBrandOptions([{ slug: 'asus', name: 'ASUS', count: 7 }], ['msi']);

    expect(options.map(formatOptionLabel)).toEqual(['ASUS (7)', 'msi (0)']);
  });

  it('thương hiệu đã chọn lấy tên từ bảng tra theo slug khi facets không còn', () => {
    const options = buildBrandOptions([{ slug: 'asus', name: 'ASUS', count: 7 }], ['msi'], {
      msi: 'MSI Gaming',
    });

    expect(options.map(formatOptionLabel)).toEqual(['ASUS (7)', 'MSI Gaming (0)']);
  });

  it('bảng tra không có slug thì mới hiện slug', () => {
    const options = buildBrandOptions([], ['msi'], { asus: 'ASUS' });

    expect(options.map(formatOptionLabel)).toEqual(['msi (0)']);
  });

  it('tên trong facets thắng bảng tra', () => {
    const options = buildBrandOptions([{ slug: 'asus', name: 'ASUS', count: 7 }], ['asus'], {
      asus: 'Tên cũ',
    });

    expect(options.map(formatOptionLabel)).toEqual(['ASUS (7)']);
  });

  it('giá trị đã chọn còn trong facets không bị nhân đôi', () => {
    const options = buildAttributeOptions(ramFacet, ['16']);

    expect(options).toHaveLength(2);
  });

  it('boolean đã chọn mà facets không còn giá trị đó vẫn hiện số đếm 0', () => {
    const facet: AttributeFacet = {
      key: 'touch',
      label: 'Cảm ứng',
      type: 'boolean',
      values: [{ value: true, count: 2 }],
    };

    expect(buildAttributeOptions(facet, ['false']).map(formatOptionLabel)).toEqual([
      'Có (2)',
      'Không (0)',
    ]);
  });

  it('facet không có danh sách giá trị vẫn hiện giá trị đã chọn', () => {
    const facet: AttributeFacet = { key: 'cpu', label: 'CPU', type: 'text', values: null };

    expect(buildAttributeOptions(facet, ['Core i7']).map(formatOptionLabel)).toEqual([
      'Core i7 (0)',
    ]);
  });
});

describe('tiêu đề nhóm bộ lọc', () => {
  it('thêm đơn vị vào tiêu đề của thuộc tính số', () => {
    const facet: AttributeFacet = {
      key: 'screen',
      label: 'Màn hình',
      type: 'number',
      unit: 'inch',
      min: 13,
      max: 17,
    };

    expect(attributeLegend(facet)).toBe('Màn hình (inch)');
  });

  it('giữ nguyên tên khi không có đơn vị', () => {
    expect(attributeLegend({ key: 'cpu', label: 'CPU', type: 'text' })).toBe('CPU');
  });
});
