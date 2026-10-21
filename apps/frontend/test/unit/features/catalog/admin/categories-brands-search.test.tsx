// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { BrandsPage } from '../../../../../src/features/catalog/admin/brands-page';
import { CategoriesPage } from '../../../../../src/features/catalog/admin/categories-page';
import { findField, renderCatalogPage, type CatalogListHarness } from './catalog-list-test-kit';

let harness: CatalogListHarness;

afterEach(async () => {
  await harness.cleanup();
});

function category(id: string, name: string, slug: string, parentId: string | null = null) {
  return {
    id,
    parentId,
    name,
    slug,
    position: 0,
    attributes: [],
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
  };
}

const categories = [
  category('dt', 'Điện thoại', 'dien-thoai'),
  category('dt-ios', 'iPhone', 'iphone', 'dt'),
  category('dt-android', 'Android', 'android', 'dt'),
  category('lt', 'Laptop', 'laptop'),
];

function openCategories(url: string, data = categories) {
  return renderCatalogPage({
    element: <CategoriesPage />,
    routePath: '/admin/categories',
    url,
    respond: () => data,
  });
}

function openBrands(url: string, data: unknown[]) {
  return renderCatalogPage({
    element: <BrandsPage />,
    routePath: '/admin/brands',
    url,
    respond: () => data,
  });
}

// Tên các danh mục đang hiện trên cây, theo thứ tự trong trang.
function visibleCategoryNames() {
  return [
    ...harness.container.querySelectorAll('.category-node strong, .category-child strong'),
  ].map((element) => element.textContent);
}

const categorySearchLabel = 'Tìm theo tên hoặc slug danh mục';
const brandSearchLabel = 'Tìm theo tên hoặc slug thương hiệu';

describe('màn danh mục có ô tìm kiếm', () => {
  it('mở link có từ khóa không dấu thì chỉ hiện danh mục khớp và ô hiện đúng từ khóa', async () => {
    harness = await openCategories('/admin/categories?search=dien%20thoai');

    expect(visibleCategoryNames()).toEqual(['Điện thoại', 'Android', 'iPhone']);
    expect(findField(harness.container, categorySearchLabel)!.value).toBe('dien thoai');
  });

  it('chỉ danh mục con khớp thì giữ danh mục gốc và chỉ con khớp', async () => {
    harness = await openCategories('/admin/categories?search=iphone');

    expect(visibleCategoryNames()).toEqual(['Điện thoại', 'iPhone']);
  });

  it('không có từ khóa thì hiện toàn bộ cây', async () => {
    harness = await openCategories('/admin/categories');

    expect(visibleCategoryNames()).toEqual(['Điện thoại', 'Android', 'iPhone', 'Laptop']);
  });

  it('từ khóa không khớp hiện thông báo không có kết quả phù hợp', async () => {
    harness = await openCategories('/admin/categories?search=xe%20may');

    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Không có kết quả phù hợp với bộ lọc.',
    );
    expect(visibleCategoryNames()).toEqual([]);
  });

  it('chưa có danh mục nào và không có từ khóa thì giữ thông báo cũ', async () => {
    harness = await openCategories('/admin/categories', []);

    expect(harness.container.textContent).toContain(
      'Chưa có danh mục nào. Hãy tạo danh mục gốc đầu tiên.',
    );
  });
});

describe('màn thương hiệu có ô tìm kiếm', () => {
  const brands = [
    { id: '1', name: 'Điện Máy Xanh', slug: 'dien-may-xanh', logoPublicId: null },
    { id: '2', name: 'Apple', slug: 'apple', logoPublicId: null },
  ];

  function brandNames() {
    return [...harness.container.querySelectorAll('tbody tr strong')].map(
      (element) => element.textContent,
    );
  }

  it('mở link có từ khóa thì chỉ hiện thương hiệu khớp và ô hiện đúng từ khóa', async () => {
    harness = await openBrands('/admin/brands?search=dien%20may', brands);

    expect(brandNames()).toEqual(['Điện Máy Xanh']);
    expect(findField(harness.container, brandSearchLabel)!.value).toBe('dien may');
  });

  it('khớp theo slug', async () => {
    harness = await openBrands('/admin/brands?search=apple', brands);

    expect(brandNames()).toEqual(['Apple']);
  });

  it('từ khóa không khớp hiện thông báo không có kết quả phù hợp', async () => {
    harness = await openBrands('/admin/brands?search=nokia', brands);

    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Không có kết quả phù hợp với bộ lọc.',
    );
  });

  it('chưa có thương hiệu nào và không có từ khóa thì giữ thông báo cũ', async () => {
    harness = await openBrands('/admin/brands', []);

    expect(harness.container.textContent).toContain(
      'Chưa có thương hiệu nào. Hãy tạo thương hiệu đầu tiên.',
    );
  });
});

describe('màn danh mục đánh số theo cây đang hiện', () => {
  // Cặp số và tên của từng dòng trên cây, theo thứ tự trong trang.
  function numberedNames() {
    return [...harness.container.querySelectorAll('.category-node-main')].map((line) => [
      line.querySelector('.category-number')!.textContent,
      line.querySelector('strong')!.textContent,
    ]);
  }

  it('hai gốc, gốc đầu có hai con: số là 1, 1.1, 1.2, 2', async () => {
    harness = await openCategories('/admin/categories');

    expect(numberedNames()).toEqual([
      ['1', 'Điện thoại'],
      ['1.1', 'Android'],
      ['1.2', 'iPhone'],
      ['2', 'Laptop'],
    ]);
  });

  it('từ khóa chỉ khớp con thứ hai của gốc đầu thì gốc hiện 1 và con đó hiện 1.1', async () => {
    harness = await openCategories('/admin/categories?search=iphone');

    expect(numberedNames()).toEqual([
      ['1', 'Điện thoại'],
      ['1.1', 'iPhone'],
    ]);
  });

  it('lọc giữ gốc thứ hai và bỏ gốc đầu thì gốc thứ hai hiện 1', async () => {
    harness = await openCategories('/admin/categories?search=laptop');

    expect(numberedNames()).toEqual([['1', 'Laptop']]);
  });

  it('số nằm trong phần tử riêng trước tên, và tên nút Sửa, Xóa không có số', async () => {
    harness = await openCategories('/admin/categories');

    const firstLine = harness.container.querySelector('.category-node-main')!;
    expect(firstLine.firstElementChild!.classList.contains('category-number')).toBe(true);
    const editButton = harness.container.querySelector('button[aria-label^="Sửa"]')!;
    expect(editButton.getAttribute('aria-label')).toBe('Sửa Điện thoại');
  });
});

describe('màn thương hiệu có cột STT', () => {
  const brands = [
    { id: '1', name: 'Apple', slug: 'apple', logoPublicId: null },
    { id: '2', name: 'Asus', slug: 'asus', logoPublicId: null },
    { id: '3', name: 'Samsung', slug: 'samsung', logoPublicId: null },
  ];

  function rowNumbers() {
    return [...harness.container.querySelectorAll('tbody tr')].map(
      (row) => row.firstElementChild!.textContent,
    );
  }

  it('cột đầu có tiêu đề STT, tiêu đề và ô đều có class num', async () => {
    harness = await openBrands('/admin/brands', brands);

    const headerCell = harness.container.querySelector('thead th')!;
    expect(headerCell.textContent).toBe('STT');
    expect(headerCell.getAttribute('scope')).toBe('col');
    expect(headerCell.classList.contains('num')).toBe(true);
    expect(headerCell.classList.contains('col-row-number')).toBe(true);
    const firstCell = harness.container.querySelector('tbody tr')!.firstElementChild!;
    expect(firstCell.tagName).toBe('TD');
    expect(firstCell.classList.contains('num')).toBe(true);
  });

  it('ba dòng có STT 1, 2, 3', async () => {
    harness = await openBrands('/admin/brands', brands);

    expect(rowNumbers()).toEqual(['1', '2', '3']);
  });

  it('từ khóa chỉ giữ dòng thứ ba ban đầu thì dòng còn lại có STT 1', async () => {
    harness = await openBrands('/admin/brands?search=samsung', brands);

    expect(rowNumbers()).toEqual(['1']);
    expect(harness.container.querySelector('tbody tr strong')!.textContent).toBe('Samsung');
  });
});
