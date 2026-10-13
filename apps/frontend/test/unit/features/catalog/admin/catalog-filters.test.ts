import { describe, expect, it } from 'vitest';
import {
  buildCategoryTree,
  filterBrands,
  filterCategoryTree,
} from '../../../../../src/features/catalog/admin/catalog-filters';

function category(id: string, name: string, parentId: string | null = null, position = 0) {
  return { id, name, slug: name.toLowerCase().replaceAll(' ', '-'), parentId, position };
}

const categories = [
  category('dt', 'Điện thoại'),
  category('dt-ios', 'iPhone', 'dt'),
  category('dt-android', 'Android', 'dt'),
  category('lt', 'Laptop'),
  category('lt-gaming', 'Laptop gaming', 'lt'),
  category('pk', 'Phụ kiện'),
];

const ids = (list: { id: string }[]) => list.map((item) => item.id);

describe('filterCategoryTree', () => {
  it('từ khóa rỗng hoặc chỉ có khoảng trắng trả nguyên danh sách', () => {
    expect(filterCategoryTree(categories, '')).toBe(categories);
    expect(filterCategoryTree(categories, '   ')).toBe(categories);
  });

  it('gõ dien thoai khớp danh mục Điện thoại không phân biệt dấu và hoa thường', () => {
    expect(ids(filterCategoryTree(categories, 'DIEN THOAI')).includes('dt')).toBe(true);
  });

  it('danh mục gốc khớp thì giữ mọi danh mục con của nó', () => {
    const result = filterCategoryTree(categories, 'dien thoai');

    expect(ids(result)).toEqual(['dt', 'dt-ios', 'dt-android']);
  });

  it('chỉ danh mục con khớp thì giữ danh mục gốc và chỉ các con khớp', () => {
    const result = filterCategoryTree(categories, 'iphone');

    expect(ids(result)).toEqual(['dt', 'dt-ios']);
  });

  it('khớp theo slug', () => {
    const result = filterCategoryTree(categories, 'phu-kien');

    expect(ids(result)).toEqual(['pk']);
  });

  it('không khớp danh mục nào thì trả danh sách rỗng', () => {
    expect(filterCategoryTree(categories, 'xe may')).toEqual([]);
  });

  it('giữ thứ tự gốc của danh sách đầu vào', () => {
    const result = filterCategoryTree(categories, 'laptop');

    expect(ids(result)).toEqual(['lt', 'lt-gaming']);
  });
});

describe('buildCategoryTree', () => {
  it('sắp danh mục gốc và danh mục con theo vị trí rồi theo tên', () => {
    const tree = buildCategoryTree([
      category('b', 'B', null, 1),
      category('a', 'A', null, 1),
      category('z', 'Z', null, 0),
      category('c2', 'C2', 'a', 0),
      category('c1', 'C1', 'a', 0),
    ]);

    expect(ids(tree.roots)).toEqual(['z', 'a', 'b']);
    expect(ids(tree.childrenByParent.a ?? [])).toEqual(['c1', 'c2']);
  });
});

describe('filterBrands', () => {
  const brands = [
    { id: '1', name: 'Điện Máy Xanh', slug: 'dien-may-xanh' },
    { id: '2', name: 'Apple', slug: 'apple-vn' },
    { id: '3', name: 'Samsung', slug: 'samsung' },
  ];

  it('từ khóa rỗng trả nguyên danh sách', () => {
    expect(filterBrands(brands, '')).toBe(brands);
  });

  it('khớp theo tên không dấu', () => {
    expect(ids(filterBrands(brands, 'dien may'))).toEqual(['1']);
  });

  it('khớp theo slug', () => {
    expect(ids(filterBrands(brands, 'apple-vn'))).toEqual(['2']);
  });

  it('không khớp thì trả danh sách rỗng', () => {
    expect(filterBrands(brands, 'nokia')).toEqual([]);
  });
});
