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
