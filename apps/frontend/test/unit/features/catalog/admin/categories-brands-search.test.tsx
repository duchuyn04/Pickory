// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { BrandsPage } from '../../../../../src/features/catalog/admin/brands-page';
import { CategoriesPage } from '../../../../../src/features/catalog/admin/categories-page';
import {
  findField,
  renderCatalogPage,
  selectOption,
  type CatalogListHarness,
} from './catalog-list-test-kit';

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

describe('màn thương hiệu lọc, sắp xếp và phân trang', () => {
  const brands = Array.from({ length: 12 }, (_, index) => {
    const number = index + 1;
    const suffix = String(number).padStart(2, '0');
    return {
      id: String(number),
      name: `Brand ${suffix}`,
      slug: `brand-${suffix}`,
      logoPublicId: number % 2 === 0 ? `pickory/brands/brand-${suffix}` : null,
      createdAt: `2026-01-${suffix}T00:00:00Z`,
      updatedAt: `2026-01-${suffix}T00:00:00Z`,
    };
  });

  function rows() {
    return [...harness.container.querySelectorAll('tbody tr')];
  }

  function brandNames() {
    return rows().map((row) => row.querySelector('strong')!.textContent);
  }

  function rowNumbers() {
    return rows().map((row) => row.firstElementChild!.textContent);
  }

  function buttonByText(text: string) {
    return [...harness.container.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent === text,
    )!;
  }

  it('mở URL lọc Có logo thì chỉ hiện thương hiệu có logo và nhãn bộ lọc', async () => {
    harness = await openBrands('/admin/brands?logo=with-logo', brands);

    expect(brandNames()).toEqual([
      'Brand 02',
      'Brand 04',
      'Brand 06',
      'Brand 08',
      'Brand 10',
      'Brand 12',
    ]);
    expect(findField(harness.container, 'Logo')!.value).toBe('with-logo');
    const filterBar = harness.container.querySelector('[aria-label="Bộ lọc đang áp dụng"]')!;
    expect(filterBar.textContent).toContain('Logo: Có logo');
    expect(filterBar.textContent).toContain('6 thương hiệu');
  });

  it('đổi bộ lọc thì xóa số trang và cập nhật URL', async () => {
    harness = await openBrands('/admin/brands?page=2', brands);

    await selectOption(findField(harness.container, 'Logo')!, 'with-logo');

    const searchParams = new URLSearchParams(harness.currentSearch());
    expect(searchParams.get('logo')).toBe('with-logo');
    expect(searchParams.has('page')).toBe(false);
    expect(brandNames()).toHaveLength(6);
  });

  it('mỗi trang có 10 dòng và trang sau tiếp tục STT của trang trước', async () => {
    harness = await openBrands('/admin/brands', brands);

    expect(rows()).toHaveLength(10);
    expect(
      harness.container.querySelector('nav[aria-label="Phân trang thương hiệu"]')!.textContent,
    ).toContain('Trang 1 / 2');
    expect(rowNumbers().at(-1)).toBe('10');

    await act(async () => buttonByText('Trang sau').click());

    expect(new URLSearchParams(harness.currentSearch()).get('page')).toBe('2');
    expect(brandNames()).toEqual(['Brand 11', 'Brand 12']);
    expect(rowNumbers()).toEqual(['11', '12']);
  });

  it('mở lại URL có sắp xếp và số trang thì hiện đúng phần danh sách', async () => {
    harness = await openBrands('/admin/brands?sort=name-desc&page=2', brands);

    expect(findField(harness.container, 'Sắp xếp')!.value).toBe('name-desc');
    expect(brandNames()).toEqual(['Brand 02', 'Brand 01']);
    expect(rowNumbers()).toEqual(['11', '12']);
  });

  it('trang vượt giới hạn có nút quay về trang 1', async () => {
    harness = await openBrands('/admin/brands?page=3', brands);

    expect(rows()).toHaveLength(0);
    await act(async () => buttonByText('Về trang 1').click());

    expect(new URLSearchParams(harness.currentSearch()).has('page')).toBe(false);
    expect(rows()).toHaveLength(10);
    expect(rowNumbers()[0]).toBe('1');
  });

  it('xóa bộ lọc thì đặt lại tìm kiếm, logo, sắp xếp và số trang', async () => {
    harness = await openBrands(
      '/admin/brands?search=brand&logo=with-logo&sort=name-desc&page=2',
      brands,
    );

    await act(async () => buttonByText('Xóa bộ lọc').click());

    expect(harness.currentSearch()).toBe('');
    expect(findField(harness.container, brandSearchLabel)!.value).toBe('');
    expect(findField(harness.container, 'Logo')!.value).toBe('');
    expect(findField(harness.container, 'Sắp xếp')!.value).toBe('');
    expect(rows()).toHaveLength(10);
  });
});
