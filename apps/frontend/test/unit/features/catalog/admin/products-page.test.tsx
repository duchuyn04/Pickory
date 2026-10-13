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
