import { describe, expect, it } from 'vitest';
import type {
  CompareAttributeGroup,
  ComparedProduct,
  CompareSpecValue,
} from '../../../../../src/features/catalog/customer/catalog-api';
import {
  buildCompareGroups,
  findMissingIds,
} from '../../../../../src/features/catalog/customer/compare-rows';

function product(id: string, specs: Record<string, CompareSpecValue>): ComparedProduct {
  return {
    summary: {
      id,
      slug: id,
      name: `Sản phẩm ${id}`,
      brand: { id: 'b', name: 'B', slug: 'b' },
      minPrice: 1000,
      inStock: true,
      status: 'active',
      image: null,
    },
    specs,
  };
}

const groups: CompareAttributeGroup[] = [
  {
    name: 'Cấu hình',
    attributes: [
      {
        key: 'ram',
        label: 'RAM',
        group: 'Cấu hình',
        type: 'enum',
        unit: 'GB',
        options: ['8', '16'],
        required: false,
        filterable: true,
      },
      {
        key: 'os',
        label: 'Hệ điều hành',
        group: 'Cấu hình',
        type: 'text',
        unit: null,
        options: null,
        required: false,
        filterable: false,
      },
    ],
  },
  {
    name: 'Hiển thị',
    attributes: [
      {
        key: 'touch',
        label: 'Cảm ứng',
        group: 'Hiển thị',
        type: 'boolean',
        unit: null,
        options: null,
        required: false,
        filterable: true,
      },
    ],
  },
];

describe('buildCompareGroups', () => {
  it('format giá trị theo thuộc tính và giữ thứ tự nhóm, thứ tự sản phẩm', () => {
    const result = buildCompareGroups(groups, [
      product('a', { ram: '8', os: 'Windows', touch: true }),
      product('b', { ram: '16', os: 'Windows', touch: false }),
    ]);
    expect(result.map((group) => group.name)).toEqual(['Cấu hình', 'Hiển thị']);
    expect(result[0]?.rows[0]?.cells).toEqual(['8 GB', '16 GB']);
    expect(result[1]?.rows[0]?.cells).toEqual(['Có', 'Không']);
  });

  it('đánh dấu dòng có giá trị khác nhau và không đánh dấu dòng giống nhau', () => {
    const result = buildCompareGroups(groups, [
      product('a', { ram: '8', os: 'Windows', touch: true }),
      product('b', { ram: '16', os: 'Windows', touch: true }),
    ]);
    const rows = result.flatMap((group) => group.rows);
    expect(rows.map((row) => [row.key, row.differs])).toEqual([
      ['ram', true],
      ['os', false],
      ['touch', false],
    ]);
  });

  it('coi null khác mọi giá trị có thật', () => {
    const result = buildCompareGroups(groups, [
      product('a', { ram: '8', os: 'Windows', touch: true }),
      product('b', { ram: '8', os: null, touch: true }),
    ]);
    const osRow = result[0]?.rows.find((row) => row.key === 'os');
    expect(osRow?.cells).toEqual(['Windows', null]);
    expect(osRow?.differs).toBe(true);
  });

  it('ẩn dòng mà mọi sản phẩm đều null', () => {
    const result = buildCompareGroups(groups, [
      product('a', { ram: '8', os: null, touch: true }),
      product('b', { ram: '16', os: null, touch: true }),
    ]);
    expect(result[0]?.rows.map((row) => row.key)).toEqual(['ram']);
  });

  it('ẩn nhóm không còn dòng nào', () => {
    const result = buildCompareGroups(groups, [
      product('a', { ram: '8', os: 'x', touch: null }),
      product('b', { ram: '8', os: 'x', touch: null }),
    ]);
    expect(result.map((group) => group.name)).toEqual(['Cấu hình']);
  });

  it('danh sách chỉ có 1 sản phẩm thì không dòng nào khác nhau', () => {
    const result = buildCompareGroups(groups, [product('a', { ram: '8', os: null, touch: true })]);
    const rows = result.flatMap((group) => group.rows);
    expect(rows.length).toBeGreaterThan(0);
    expect(rows.every((row) => !row.differs)).toBe(true);
  });

  it('so sánh giá trị đã format, nên cùng chữ thì giống nhau', () => {
    const result = buildCompareGroups(groups, [
      product('a', { ram: '8', os: 'Windows', touch: true }),
      product('b', { ram: '8', os: 'Windows', touch: true }),
    ]);
    expect(result.flatMap((group) => group.rows).some((row) => row.differs)).toBe(false);
  });

  it('trả mảng rỗng khi không có sản phẩm', () => {
    expect(buildCompareGroups(groups, [])).toEqual([]);
  });
});

describe('findMissingIds', () => {
  it('trả các id đã yêu cầu mà API không trả về, giữ thứ tự', () => {
    const products = [product('a', {}), product('c', {})];
    expect(findMissingIds(['a', 'b', 'c', 'd'], products)).toEqual(['b', 'd']);
  });

  it('trả mảng rỗng khi API trả đủ', () => {
    expect(findMissingIds(['a'], [product('a', {})])).toEqual([]);
  });
});
