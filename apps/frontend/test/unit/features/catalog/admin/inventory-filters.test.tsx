// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { InventoryPage } from '../../../../../src/features/catalog/admin/inventory-page';
import {
  findField,
  queryOf,
  renderCatalogPage,
  type CatalogListHarness,
} from './catalog-list-test-kit';

let harness: CatalogListHarness;

afterEach(async () => {
  await harness.cleanup();
});

function lowStockSku(id: string) {
  return {
    skuId: `sku-${id}`,
    code: `ABC-${id}`,
    productId: 'prod-1',
    productName: 'Áo thun',
    optionValues: {},
    stock: 2,
  };
}

function open(url: string, items: unknown[] = [lowStockSku('1')], total = items.length) {
  return renderCatalogPage({
    element: <InventoryPage />,
    routePath: '/admin/inventory',
    url,
    respond: () => ({ items, total, page: 1, limit: 20 }),
  });
}

function lowStockRequests() {
  return harness.requestedUrls.filter((url) => url.startsWith('/api/admin/inventory/low-stock'));
}

describe('màn tồn kho giữ từ khóa trên URL', () => {
  it('mở link có từ khóa thì gửi search và ô hiện đúng giá trị', async () => {
    harness = await open('/admin/inventory?search=abc');

    expect(queryOf(lowStockRequests()[0]!)).toEqual({ page: '1', limit: '20', search: 'abc' });
    expect(findField(harness.container, 'Tìm theo mã SKU hoặc tên sản phẩm')!.value).toBe('abc');
  });

  it('không có từ khóa thì không gửi search', async () => {
    harness = await open('/admin/inventory');

    expect(queryOf(lowStockRequests()[0]!)).toEqual({ page: '1', limit: '20' });
  });

  it('danh sách rỗng khi có từ khóa hiện thông báo không có kết quả phù hợp', async () => {
    harness = await open('/admin/inventory?search=zzz', []);

    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Không có kết quả phù hợp với bộ lọc.',
    );
  });

  it('danh sách rỗng khi không có từ khóa giữ thông báo cũ của màn', async () => {
    harness = await open('/admin/inventory', []);

    expect(harness.container.textContent).toContain('Không có SKU nào sắp hết hàng.');
  });

  it('mở link vượt quá số trang hiện nút Về trang 1 và nút xóa page khỏi URL', async () => {
    harness = await open('/admin/inventory?page=99', [], 30);
    const button = [...harness.container.querySelectorAll('button')].find(
      (candidate) => candidate.textContent === 'Về trang 1',
    )!;

    expect(button).toBeDefined();
    expect(harness.currentSearch()).toBe('?page=99');
  });
});
