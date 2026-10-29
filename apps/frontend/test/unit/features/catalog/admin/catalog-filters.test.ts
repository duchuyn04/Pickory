import { describe, expect, it } from 'vitest';
import {
  buildCategoryTree,
  filterAndSortBrands,
  filterBrands,
  filterCategoryTree,
} from '../../../../../src/features/catalog/admin/catalog-filters';

describe('filterAndSortBrands', () => {
  const brands = [
    {
      id: '1',
      name: 'Samsung',
      slug: 'samsung',
      logoPublicId: null,
      createdAt: '2026-01-01T00:00:00Z',
    },
    {
      id: '2',
      name: 'Apple',
      slug: 'apple',
      logoPublicId: 'pickory/brands/apple',
      createdAt: '2026-03-01T00:00:00Z',
    },
    {
      id: '3',
      name: 'Asus',
      slug: 'asus',
      logoPublicId: '   ',
      createdAt: '2026-02-01T00:00:00Z',
    },
  ];

  it('chỉ giữ thương hiệu có mã logo khi lọc Có logo', () => {
    const result = filterAndSortBrands(brands, { keyword: '', logo: 'with-logo', sort: '' });

    expect(ids(result)).toEqual(['2']);
  });

  it('coi mã logo rỗng hoặc chỉ có khoảng trắng là Chưa có logo', () => {
    const result = filterAndSortBrands(brands, { keyword: '', logo: 'without-logo', sort: '' });

    expect(ids(result)).toEqual(['3', '1']);
  });

  it('kết hợp từ khóa với bộ lọc logo', () => {
    const result = filterAndSortBrands(brands, {
      keyword: 'samsung',
      logo: 'without-logo',
      sort: '',
    });

    expect(ids(result)).toEqual(['1']);
  });

  it('sắp xếp tên theo cả hai chiều mà không đổi danh sách đầu vào', () => {
    const ascending = filterAndSortBrands(brands, { keyword: '', logo: '', sort: 'name-asc' });
    const descending = filterAndSortBrands(brands, { keyword: '', logo: '', sort: 'name-desc' });

    expect(ids(ascending)).toEqual(['2', '3', '1']);
    expect(ids(descending)).toEqual(['1', '3', '2']);
    expect(ids(brands)).toEqual(['1', '2', '3']);
  });

  it('sắp xếp ngày tạo theo cả hai chiều', () => {
    const newest = filterAndSortBrands(brands, { keyword: '', logo: '', sort: 'newest' });
    const oldest = filterAndSortBrands(brands, { keyword: '', logo: '', sort: 'oldest' });

    expect(ids(newest)).toEqual(['2', '3', '1']);
    expect(ids(oldest)).toEqual(['1', '3', '2']);
  });

  it('giá trị lọc lạ không làm mất dữ liệu và giá trị sắp xếp lạ dùng thứ tự tên', () => {
    const result = filterAndSortBrands(brands, {
      keyword: '',
      logo: 'unexpected',
      sort: 'unexpected',
    });

    expect(ids(result)).toEqual(['2', '3', '1']);
  });
});

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
