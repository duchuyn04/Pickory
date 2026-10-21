// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ProductsPage } from '../../../../../src/features/catalog/admin/products-page';
import {
  advance,
  findField,
  queryOf,
  renderCatalogPage,
  settle,
  typeInto,
  type CatalogListHarness,
} from './catalog-list-test-kit';

let harness: CatalogListHarness;

afterEach(async () => {
  vi.useRealTimers();
  await harness.cleanup();
});

function product(id: string) {
  return {
    id,
    name: `Áo ${id}`,
    categoryId: 'cat-1',
    status: 'active',
    minPrice: 100000,
    variants: [{ id: `sku-${id}`, code: `AO-${id}` }],
    images: [],
  };
}

function productsPage(items: unknown[], total = items.length, page = 1) {
  return { items, total, page, limit: 20 };
}

function open(url: string, items: unknown[] = [product('1')], total = items.length) {
  return renderCatalogPage({
    element: <ProductsPage />,
    routePath: '/admin/products',
    url,
    respond: (requested) => {
      if (requested.pathname === '/api/admin/products') return productsPage(items, total);
      if (requested.pathname === '/api/admin/categories') {
        return [{ id: 'cat-1', name: 'Áo', parentId: null }];
      }
      return [];
    },
  });
}

function productRequests() {
  return harness.requestedUrls.filter((url) => url.startsWith('/api/admin/products'));
}

const searchLabel = 'Tên hoặc mã SKU';

describe('màn sản phẩm giữ bộ lọc trên URL', () => {
  it('mở link có bộ lọc thì gửi đúng tham số và các ô hiện đúng giá trị', async () => {
    harness = await open('/admin/products?search=ao&status=active&lowStock=true&page=2');

    expect(queryOf(productRequests()[0]!)).toEqual({
      page: '2',
      limit: '20',
      search: 'ao',
      status: 'active',
      lowStock: 'true',
    });
    expect(findField(harness.container, searchLabel)!.value).toBe('ao');
    expect(findField(harness.container, 'Trạng thái')!.value).toBe('active');
    expect(findField(harness.container, 'Sắp hết hàng')!.checked).toBe(true);
  });

  it('bật rồi tắt ô sắp hết hàng thì URL có rồi mất lowStock', async () => {
    harness = await open('/admin/products');
    const checkbox = findField(harness.container, 'Sắp hết hàng')!;

    await act(async () => checkbox.click());
    expect(new URLSearchParams(harness.currentSearch()).get('lowStock')).toBe('true');
    await settle();
    expect(queryOf(productRequests().at(-1)!).lowStock).toBe('true');

    await act(async () => checkbox.click());
    expect(new URLSearchParams(harness.currentSearch()).has('lowStock')).toBe(false);
    await settle();
    expect(queryOf(productRequests().at(-1)!)).not.toHaveProperty('lowStock');
  });

  it('gõ nhiều ký tự liền nhau chỉ gửi một request mới', async () => {
    harness = await open('/admin/products');
    const requestsBefore = productRequests().length;
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const input = findField(harness.container, searchLabel)!;

    await typeInto(input, 'a');
    await advance(100);
    await typeInto(input, 'ao');
    await advance(100);
    await typeInto(input, 'aop');
    // Chưa đủ 300 ms từ lần gõ cuối nên chưa có request nào.
    await advance(299);
    expect(productRequests()).toHaveLength(requestsBefore);

    await advance(1);
    await advance(0);

    expect(productRequests()).toHaveLength(requestsBefore + 1);
    expect(queryOf(productRequests().at(-1)!).search).toBe('aop');
  });

  it('đổi bộ lọc xóa page khỏi URL', async () => {
    harness = await open('/admin/products?page=3');
    const select = findField(harness.container, 'Trạng thái')!;
    const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!;

    await act(async () => {
      setter.call(select, 'hidden');
      select.dispatchEvent(new Event('change', { bubbles: true }));
    });

    expect(new URLSearchParams(harness.currentSearch()).has('page')).toBe(false);
    expect(new URLSearchParams(harness.currentSearch()).get('status')).toBe('hidden');
  });

  it('danh sách rỗng khi có bộ lọc hiện thông báo không có kết quả phù hợp', async () => {
    harness = await open('/admin/products?search=zzz', []);

    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Không có kết quả phù hợp với bộ lọc.',
    );
  });

  it('danh sách rỗng khi không có bộ lọc giữ thông báo cũ của màn', async () => {
    harness = await open('/admin/products', []);

    expect(harness.container.textContent).toContain('Không có sản phẩm phù hợp.');
    expect(harness.container.textContent).not.toContain('Không có kết quả phù hợp với bộ lọc.');
  });

  it('bấm Trang sau ghi page=2 lên URL', async () => {
    harness = await open('/admin/products', [product('1')], 45);
    const next = [...harness.container.querySelectorAll('button')].find(
      (button) => button.textContent === 'Trang sau',
    )!;

    await act(async () => next.click());

    expect(new URLSearchParams(harness.currentSearch()).get('page')).toBe('2');
  });
});

// Nhóm bộ lọc đang áp dụng, nằm giữa hàng lọc và bảng. `null` khi thanh không render.
function getFilterBar() {
  return harness.container.querySelector<HTMLElement>('[aria-label="Bộ lọc đang áp dụng"]');
}

function filterLabels() {
  return [...getFilterBar()!.querySelectorAll('li')].map(
    (item) => item.firstElementChild!.textContent,
  );
}

function buttonByName(name: string) {
  return harness.container.querySelector<HTMLButtonElement>(`button[aria-label="${name}"]`);
}

function searchParamsOfUrl() {
  return new URLSearchParams(harness.currentSearch());
}

// Nhấn Enter trong ô tìm để gửi từ khóa ngay, không phải chờ 300 ms của bộ đếm gõ.
async function submitSearch(text: string) {
  const input = findField(harness.container, searchLabel)!;
  await typeInto(input, text);
  await act(async () => {
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
  });
}

describe('màn sản phẩm giữ bảng cũ khi tải kết quả mới', () => {
  // Response của từ khóa `dien` bị giữ bằng một lời hứa do test điều khiển, không dùng thời gian chờ thật.
  async function openWithHeldSearch() {
    let releaseSearchResponse!: () => void;
    const searchResponseGate = new Promise<void>((resolve) => (releaseSearchResponse = resolve));
    harness = await renderCatalogPage({
      element: <ProductsPage />,
      routePath: '/admin/products',
      url: '/admin/products',
      respond: async (requested) => {
        if (requested.pathname === '/api/admin/categories') return [];
        if (requested.searchParams.get('search') === 'dien') {
          await searchResponseGate;
          return productsPage([product('moi')]);
        }
        return productsPage([product('cu')]);
      },
    });
    return releaseSearchResponse;
  }

  function busyBlock() {
    return harness.container.querySelector<HTMLElement>('[aria-busy="true"]');
  }

  it('đổi từ khóa khi response mới chưa về thì bảng cũ còn, khối bảng có aria-busy và ô tìm báo đang tải', async () => {
    const releaseSearchResponse = await openWithHeldSearch();
    expect(busyBlock()).toBeNull();

    await submitSearch('dien');

    expect(harness.container.textContent).toContain('Áo cu');
    expect(busyBlock()).not.toBeNull();
    expect(busyBlock()!.querySelector('table')).not.toBeNull();
    const searchStatus = findField(harness.container, searchLabel)!
      .closest('.field')!
      .querySelector('[aria-live="polite"]')!;
    expect(searchStatus.textContent).toBe('Đang tải kết quả');

    await act(async () => releaseSearchResponse());
    await settle();

    expect(busyBlock()).toBeNull();
    expect(harness.container.textContent).toContain('Áo moi');
    expect(harness.container.textContent).not.toContain('Áo cu');
    expect(searchStatus.textContent).toBe('');
  });

  it('lần tải đầu chưa có dữ liệu vẫn hiện Đang tải sản phẩm và chưa có aria-busy', async () => {
    harness = await renderCatalogPage({
      element: <ProductsPage />,
      routePath: '/admin/products',
      url: '/admin/products',
      respond: () => new Promise(() => {}),
    });

    expect(harness.container.textContent).toContain('Đang tải sản phẩm...');
    expect(busyBlock()).toBeNull();
    expect(harness.container.querySelector('table')).toBeNull();
  });
});

describe('màn sản phẩm tô đậm chữ khớp từ khóa', () => {
  const phone = {
    ...product('1'),
    name: 'Điện thoại Samsung',
    variants: [{ id: 'sku-1', code: 'SS-DT-01' }],
  };

  it('tên có chữ khớp không dấu thì chữ đó nằm trong mark và giữ nguyên dấu', async () => {
    harness = await open('/admin/products?search=dien%20thoai', [phone]);

    const marks = [...harness.container.querySelectorAll('tbody mark')];
    expect(marks.map((mark) => mark.textContent)).toEqual(['Điện thoại']);
    expect(harness.container.querySelector('tbody th')!.textContent).toContain(
      'Điện thoại Samsung',
    );
  });

  it('mã SKU khớp từ khóa cũng nằm trong mark', async () => {
    harness = await open('/admin/products?search=dt-01', [phone]);

    const skuCell = harness.container.querySelector('tbody .cell-sub')!;
    expect([...skuCell.querySelectorAll('mark')].map((mark) => mark.textContent)).toEqual([
      'DT-01',
    ]);
    // Tên không chứa từ khóa nên không có mark ở tên.
    expect(harness.container.querySelectorAll('tbody mark')).toHaveLength(1);
  });

  it('không có từ khóa thì bảng không có mark', async () => {
    harness = await open('/admin/products', [phone]);

    expect(harness.container.querySelector('mark')).toBeNull();
  });
});

describe('thanh bộ lọc đang áp dụng', () => {
  const allFiltersUrl =
    '/admin/products?search=iphone&status=active&categoryId=cat-1&lowStock=true&page=2';

  it('không có bộ lọc thì thanh không render và dòng đếm giữ nguyên', async () => {
    harness = await open('/admin/products', [product('1')], 12);

    expect(getFilterBar()).toBeNull();
    expect(harness.container.querySelector('.product-list-head')!.textContent).toContain(
      '12 sản phẩm',
    );
  });

  it('URL có đủ 4 bộ lọc thì thanh có đủ 4 nhãn', async () => {
    harness = await open(allFiltersUrl);

    expect(filterLabels()).toEqual([
      'Từ khóa: iphone',
      'Trạng thái: Đang bán',
      'Danh mục: Áo',
      'Sắp hết hàng',
    ]);
  });

  it('trạng thái lạ hiện nguyên giá trị và danh mục chưa có tên hiện mã danh mục', async () => {
    harness = await renderCatalogPage({
      element: <ProductsPage />,
      routePath: '/admin/products',
      url: '/admin/products?status=archived&categoryId=cat-9',
      respond: (requested) =>
        requested.pathname === '/api/admin/products' ? productsPage([product('1')]) : [],
    });

    expect(filterLabels()).toEqual(['Trạng thái: archived', 'Danh mục: cat-9']);
  });

  it.each([
    ['Bỏ lọc Từ khóa: iphone', 'search'],
    ['Bỏ lọc Trạng thái: Đang bán', 'status'],
    ['Bỏ lọc Danh mục: Áo', 'categoryId'],
    ['Bỏ lọc Sắp hết hàng', 'lowStock'],
  ])('bấm %s chỉ xóa %s khỏi URL và xóa page', async (buttonName, removedName) => {
    harness = await open(allFiltersUrl);

    await act(async () => buttonByName(buttonName)!.click());

    const params = searchParamsOfUrl();
    expect(params.has(removedName)).toBe(false);
    expect(params.has('page')).toBe(false);
    const keptNames = ['search', 'status', 'categoryId', 'lowStock'].filter(
      (name) => name !== removedName,
    );
    for (const name of keptNames) expect(params.has(name)).toBe(true);
  });

  it('bỏ nhãn từ khóa thì ô tìm cũng rỗng', async () => {
    harness = await open(allFiltersUrl);
    expect(findField(harness.container, searchLabel)!.value).toBe('iphone');

    await act(async () => buttonByName('Bỏ lọc Từ khóa: iphone')!.click());

    expect(findField(harness.container, searchLabel)!.value).toBe('');
  });

  it('nút X có type button', async () => {
    harness = await open(allFiltersUrl);

    expect(buttonByName('Bỏ lọc Trạng thái: Đang bán')!.type).toBe('button');
  });

  it('bấm Xóa bộ lọc thì URL không còn bộ lọc nào, thanh ẩn và ô tìm rỗng', async () => {
    harness = await open(`${allFiltersUrl}&keep=1`);
    const clearAll = [...harness.container.querySelectorAll('button')].find(
      (button) => button.textContent === 'Xóa bộ lọc',
    )!;

    await act(async () => clearAll.click());

    const params = searchParamsOfUrl();
    for (const name of ['search', 'status', 'categoryId', 'lowStock', 'page']) {
      expect(params.has(name)).toBe(false);
    }
    // Tham số lạ không thuộc bộ lọc nên còn nguyên.
    expect(params.get('keep')).toBe('1');
    expect(getFilterBar()).toBeNull();
    expect(findField(harness.container, searchLabel)!.value).toBe('');
  });

  it('có từ khóa thì thanh hiện số kết quả kèm từ khóa', async () => {
    harness = await open('/admin/products?search=iphone', [product('1')], 12);

    expect(getFilterBar()!.textContent).toContain('12 sản phẩm khớp "iphone"');
  });

  it('chỉ có bộ lọc khác từ khóa thì thanh hiện số kết quả không kèm từ khóa', async () => {
    harness = await open('/admin/products?status=active', [product('1')], 12);

    const summary = getFilterBar()!.textContent!;
    expect(summary).toContain('12 sản phẩm');
    expect(summary).not.toContain('khớp');
  });

  it('thanh hiện thì dòng đếm ở đầu danh sách ẩn đi, số chỉ hiện một lần', async () => {
    harness = await open('/admin/products?status=active', [product('1')], 12);

    expect(harness.container.querySelector('.product-list-head')!.textContent).not.toContain(
      'sản phẩm',
    );
    expect(harness.container.textContent!.match(/12 sản phẩm/g)).toHaveLength(1);
  });
});

describe('màn sản phẩm hiện ảnh đầu tiên', () => {
  function cloudinaryImage(id: string, position: number, alt: string) {
    return {
      id,
      publicId: `pickory/products/${id}`,
      alt,
      skuId: null,
      position,
      url: `https://res.cloudinary.com/demo/image/upload/pickory/products/${id}.jpg`,
    };
  }

  function productWithImages(images: unknown[]) {
    return { ...product('1'), images };
  }

  function thumbnails() {
    return [...harness.container.querySelectorAll<HTMLImageElement>('tbody img')];
  }

  it('ảnh đi qua biến đổi Cloudinary rộng 200, không dùng URL gốc, và có kích thước cố định, lazy', async () => {
    const original = cloudinaryImage('a', 0, 'Mặt trước');
    harness = await open('/admin/products', [productWithImages([original])]);

    const [thumbnail] = thumbnails();
    expect(thumbnails()).toHaveLength(1);
    expect(thumbnail!.src).toContain('w_200');
    expect(thumbnail!.src).not.toBe(original.url);
    expect(thumbnail!.getAttribute('loading')).toBe('lazy');
    expect(thumbnail!.getAttribute('decoding')).toBe('async');
    expect(thumbnail!.getAttribute('width')).toBe('48');
    expect(thumbnail!.getAttribute('height')).toBe('48');
  });

  it('response trả mảng không theo thứ tự thì chọn ảnh có position nhỏ nhất', async () => {
    const images = [
      cloudinaryImage('hai', 2, 'Ảnh hai'),
      cloudinaryImage('khong', 0, 'Ảnh không'),
      cloudinaryImage('mot', 1, 'Ảnh một'),
    ];
    harness = await open('/admin/products', [productWithImages(images)]);

    expect(thumbnails()).toHaveLength(1);
    expect(thumbnails()[0]!.src).toContain('khong.jpg');
    expect(thumbnails()[0]!.alt).toBe('Ảnh không');
  });

  it('alt lấy từ ảnh, bỏ khoảng trắng hai đầu', async () => {
    harness = await open('/admin/products', [
      productWithImages([cloudinaryImage('a', 0, '  Mặt trước  ')]),
    ]);

    expect(thumbnails()[0]!.alt).toBe('Mặt trước');
  });

  it.each(['', '   '])('alt %j thì dùng tên sản phẩm', async (alt) => {
    harness = await open('/admin/products', [productWithImages([cloudinaryImage('a', 0, alt)])]);

    expect(thumbnails()[0]!.alt).toBe('Áo 1');
  });

  it('sản phẩm không có ảnh thì có khung giữ chỗ tên Chưa có ảnh và không có thẻ img', async () => {
    harness = await open('/admin/products', [productWithImages([])]);

    const placeholder = harness.container.querySelector<HTMLElement>(
      'tbody [role="img"][aria-label="Chưa có ảnh"]',
    );
    expect(placeholder).not.toBeNull();
    expect(thumbnails()).toHaveLength(0);
    // Biểu tượng bên trong chỉ để nhìn, nên ẩn với trình đọc màn hình.
    expect(placeholder!.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
  });

  it('mỗi dòng có ảnh hoặc khung giữ chỗ, không dòng nào có cả hai', async () => {
    harness = await open('/admin/products', [
      { ...productWithImages([cloudinaryImage('a', 0, 'Ảnh a')]), id: 'p1' },
      { ...productWithImages([]), id: 'p2' },
    ]);

    const rows = [...harness.container.querySelectorAll('tbody tr')];
    expect(rows).toHaveLength(2);
    expect(rows[0]!.querySelectorAll('img')).toHaveLength(1);
    expect(rows[0]!.querySelectorAll('[role="img"]')).toHaveLength(0);
    expect(rows[1]!.querySelectorAll('img')).toHaveLength(0);
    expect(rows[1]!.querySelectorAll('[role="img"]')).toHaveLength(1);
  });

  it('tên và mã SKU vẫn tô đậm khi dòng có ảnh', async () => {
    harness = await open('/admin/products?search=ao', [
      productWithImages([cloudinaryImage('a', 0, 'Ảnh a')]),
    ]);

    const marks = [...harness.container.querySelectorAll('tbody mark')].map(
      (mark) => mark.textContent,
    );
    expect(marks).toEqual(['Áo', 'AO']);
  });
});
