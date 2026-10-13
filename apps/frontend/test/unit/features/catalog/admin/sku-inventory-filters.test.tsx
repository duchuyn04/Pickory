// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { SkuInventoryPage } from '../../../../../src/features/catalog/admin/sku-inventory-page';
import type { InventoryMovement } from '../../../../../src/features/catalog/admin/inventory-logic';
import {
  findField,
  queryOf,
  renderCatalogPage,
  selectOption,
  settle,
  type CatalogListHarness,
} from './catalog-list-test-kit';

let harness: CatalogListHarness;

afterEach(async () => {
  await harness.cleanup();
});

function movement(id: string, type: InventoryMovement['type'], reason: string): InventoryMovement {
  return {
    id,
    delta: 3,
    type,
    orderId: null,
    actorId: 'abcdef1234567890',
    reason,
    createdAt: '2026-10-08T03:30:00Z',
  };
}

// Máy chủ giả: lọc theo `type` như API thật, và điều chỉnh tay thêm một movement ở đầu danh sách.
function createServer() {
  const state = {
    stock: 5,
    movements: [
      movement('mv-2', 'return', 'Khách trả hàng lỗi'),
      movement('mv-1', 'manual', 'Nhập kho đầu kỳ'),
    ],
  };
  const respond = (url: URL, init?: RequestInit) => {
    if (url.pathname === '/api/admin/products/p1') {
      return {
        id: 'p1',
        name: 'MacBook Air M3',
        variants: [
          { id: 'sku-1', code: 'MAC-01', optionValues: {}, stock: state.stock, enabled: true },
        ],
      };
    }
    if (url.pathname === '/api/admin/skus/sku-1/movements') {
      if (init?.method === 'POST') {
        const body = JSON.parse(init.body as string) as { delta: number; reason: string };
        const created = movement('mv-new', 'manual', body.reason);
        state.stock += body.delta;
        state.movements = [created, ...state.movements];
        return { skuId: 'sku-1', stock: state.stock, movement: created };
      }
      const type = url.searchParams.get('type');
      const items = type ? state.movements.filter((item) => item.type === type) : state.movements;
      return { items, total: items.length, page: 1, limit: 20 };
    }
    return [];
  };
  return respond;
}

function open(url: string, respond = createServer()) {
  return renderCatalogPage({
    element: <SkuInventoryPage />,
    routePath: '/admin/products/:productId/skus/:skuId/inventory',
    url,
    respond,
  });
}

const basePath = '/admin/products/p1/skus/sku-1/inventory';

function movementRequests() {
  return harness.requestedUrls.filter((url) => url.startsWith('/api/admin/skus/sku-1/movements'));
}

function historyRows() {
  return [
    ...harness.container.querySelectorAll<HTMLTableRowElement>(
      'table[aria-label="Lịch sử tồn kho"] tbody tr',
    ),
  ];
}

async function setField(selector: string, value: string) {
  const element = harness.container.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector);
  const prototype =
    element instanceof HTMLTextAreaElement
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
  await act(async () => {
    Object.getOwnPropertyDescriptor(prototype, 'value')!.set!.call(element, value);
    element!.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

describe('màn biến động kho của SKU lọc theo loại', () => {
  it('mở link có loại thì gửi type và ô chọn hiện đúng giá trị', async () => {
    harness = await open(`${basePath}?type=return`);

    expect(queryOf(movementRequests()[0]!)).toEqual({ page: '1', limit: '20', type: 'return' });
    expect(findField(harness.container, 'Loại biến động')!.value).toBe('return');
  });

  it('ô chọn có tùy chọn Tất cả và bốn loại biến động với nhãn của màn', async () => {
    harness = await open(basePath);

    const options = [...findField(harness.container, 'Loại biến động')!.options].map((option) => [
      option.value,
      option.textContent,
    ]);

    expect(options).toEqual([
      ['', 'Tất cả'],
      ['manual', 'Điều chỉnh'],
      ['order_reserve', 'Giữ hàng cho đơn'],
      ['order_release', 'Trả hàng về kho'],
      ['return', 'Khách trả hàng'],
    ]);
  });

  it('đổi loại ghi type lên URL và xóa page', async () => {
    harness = await open(`${basePath}?page=2`);

    await selectOption(findField(harness.container, 'Loại biến động')!, 'manual');

    const params = new URLSearchParams(harness.currentSearch());
    expect(params.get('type')).toBe('manual');
    expect(params.has('page')).toBe(false);
  });

  it('danh sách rỗng khi có loại hiện thông báo không có kết quả phù hợp', async () => {
    harness = await open(`${basePath}?type=order_release`);

    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Không có kết quả phù hợp với bộ lọc.',
    );
  });

  it('sau khi điều chỉnh tồn, URL không còn type và page, dòng mới nằm đầu danh sách', async () => {
    harness = await open(`${basePath}?type=return&page=2`);
    const adjustButton = [...harness.container.querySelectorAll('button')].find(
      (button) => button.textContent === 'Điều chỉnh tồn kho',
    )!;
    await act(async () => adjustButton.click());
    await setField('#adjust-delta', '7');
    await setField('#adjust-reason', 'Nhập lô mới');

    await act(async () =>
      harness.container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );
    await settle();
    await settle();

    expect(harness.currentSearch()).toBe('');
    expect(historyRows()[0]!.textContent).toContain('Nhập lô mới');
    expect(findField(harness.container, 'Loại biến động')!.value).toBe('');
  });
});
