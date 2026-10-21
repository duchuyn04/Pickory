// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SkuInventoryPage } from '../../../../../src/features/catalog/admin/sku-inventory-page';
import type { InventoryMovement } from '../../../../../src/features/catalog/admin/inventory-logic';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

let container: HTMLDivElement;
let root: Root;

const baseActor: ActorData = {
  anonymousId: 'anon',
  userId: 'user-1',
  kind: 'staff',
  roles: ['sales'],
  permissions: ['products:read'],
  consent: { personalization: false, marketing: false },
};
const writerActor: ActorData = {
  ...baseActor,
  permissions: ['products:read', 'inventory:write'],
};

const seedMovements: InventoryMovement[] = [
  {
    id: 'mv-2',
    delta: -2,
    type: 'order_reserve',
    orderId: 'order-77',
    actorId: null,
    reason: 'Giữ hàng cho đơn order-77',
    createdAt: '2026-10-08T05:00:00Z',
  },
  {
    id: 'mv-1',
    delta: 7,
    type: 'manual',
    orderId: null,
    actorId: 'abcdef1234567890',
    reason: 'Nhập kho đầu kỳ',
    createdAt: '2026-10-08T03:30:00Z',
  },
];

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

// Chờ truy vấn và render xong. React Query cần vài lượt hàng đợi vi tác vụ sau khi fetch trả về.
async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status });
}

interface ServerOptions {
  productStatus?: number;
  total?: number;
  // Phân trang mà response trả về, độc lập với tham số trên URL. Chứng minh màn đọc `page` và `limit` từ response.
  responsePaging?: { page: number; limit: number };
  skuId?: string;
  // Sau khi điều chỉnh, các lần đọc lại không bao giờ trả về. Chứng minh trang tự cập nhật bằng dữ liệu của lần điều chỉnh.
  hangAfterAdjust?: boolean;
}

// Máy chủ giả giữ trạng thái: sau khi điều chỉnh, các lần đọc kế tiếp thấy dữ liệu mới như API thật.
function createServer(options: ServerOptions = {}) {
  const state = { stock: 5, movements: [...seedMovements], adjusted: false };
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    const url = new URL(input, 'http://localhost');
    if (state.adjusted && options.hangAfterAdjust && (init?.method ?? 'GET') === 'GET') {
      return new Promise<Response>(() => {});
    }
    if (url.pathname === '/api/admin/products/p1') {
      if (options.productStatus === 404) {
        return jsonResponse(
          { error: { code: 'PRODUCT_NOT_FOUND', message: 'Không tìm thấy sản phẩm.' } },
          404,
        );
      }
      return jsonResponse({
        data: {
          id: 'p1',
          name: 'MacBook Air M3',
          variants: [
            {
              id: options.skuId ?? 'sku-1',
              code: 'MAC-01',
              optionValues: { Màu: 'Bạc' },
              stock: state.stock,
              enabled: true,
              price: 25000000,
            },
          ],
        },
      });
    }
    // `GET` và `POST` dùng chung đường dẫn, nên nhánh đọc lịch sử phải kiểm cả method.
    if (url.pathname === '/api/admin/skus/sku-1/movements' && (init?.method ?? 'GET') === 'GET') {
      const page = Number(url.searchParams.get('page') ?? '1');
      return jsonResponse({
        data: {
          items: state.movements,
          total: options.total ?? state.movements.length,
          page: options.responsePaging?.page ?? page,
          limit: options.responsePaging?.limit ?? 20,
        },
      });
    }
    if (url.pathname === '/api/admin/skus/sku-1/movements' && init?.method === 'POST') {
      const body = JSON.parse(init.body as string) as { delta: number; reason: string };
      const movement: InventoryMovement = {
        id: 'mv-new',
        delta: body.delta,
        type: 'manual',
        orderId: null,
        actorId: 'user-1',
        reason: body.reason,
        createdAt: '2026-10-08T07:00:00Z',
      };
      state.adjusted = true;
      state.stock += body.delta;
      state.movements = [movement, ...state.movements];
      return jsonResponse({ data: { skuId: 'sku-1', stock: state.stock, movement } });
    }
    return jsonResponse({ error: { code: 'NOT_FOUND', message: 'Không có route giả.' } }, 404);
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

async function renderPage(actor: ActorData) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(['actor'], actor);
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/admin/products/p1/skus/sku-1/inventory']}>
          <Routes>
            <Route
              path="/admin/products/:productId/skus/:skuId/inventory"
              element={<SkuInventoryPage />}
            />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  // Chờ các truy vấn đầu tiên trả về.
  await settle();
  return queryClient;
}

function adjustButton(): HTMLButtonElement | undefined {
  return [...document.querySelectorAll('button')].find(
    (button) => button.textContent === 'Điều chỉnh tồn kho',
  );
}

async function change(selector: string, value: string) {
  const element = document.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
  await act(async () => {
    const prototype =
      element instanceof HTMLTextAreaElement
        ? HTMLTextAreaElement.prototype
        : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, 'value')!.set!.call(element, value);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

function historyRows(): HTMLTableRowElement[] {
  return [
    ...document.querySelectorAll<HTMLTableRowElement>(
      'table[aria-label="Lịch sử tồn kho"] tbody tr',
    ),
  ];
}

const stockText = () => document.querySelector('[data-testid="sku-stock"]')?.textContent;

describe('SkuInventoryPage', () => {
  it('hiện thông tin SKU, số tồn và lịch sử cho người chỉ có quyền xem', async () => {
    createServer();
    await renderPage(baseActor);

    expect(document.querySelector('h1')?.textContent).toBe('Tồn kho MAC-01');
    expect(document.body.textContent).toContain('MacBook Air M3');
    expect(document.body.textContent).toContain('Bạc');
    expect(stockText()).toBe('5');
    const rows = historyRows();
    expect(rows).toHaveLength(2);
    const firstRow = rows[0]!.textContent;
    expect(firstRow).toContain('12:00 08/10/2026');
    expect(firstRow).toContain('Giữ hàng cho đơn');
    expect(firstRow).toContain('-2');
    expect(firstRow).toContain('order-77');
    const secondRow = rows[1]!.textContent;
    expect(secondRow).toContain('+7');
    expect(secondRow).toContain('Điều chỉnh');
    expect(secondRow).toContain('abcdef12');
  });

  it('không có quyền inventory:write thì không có nút điều chỉnh', async () => {
    createServer();
    await renderPage(baseActor);
    expect(adjustButton()).toBeUndefined();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it('có quyền inventory:write thì có nút điều chỉnh', async () => {
    createServer();
    await renderPage(writerActor);
    expect(adjustButton()).toBeDefined();
  });

  it('điều chỉnh xong thì số tồn và dòng đầu lịch sử đổi theo, focus về nút điều chỉnh', async () => {
    const fetchMock = createServer();
    await renderPage(writerActor);
    const button = adjustButton()!;
    button.focus();
    await act(async () => button.click());
    await change('#adjust-delta', '7');
    await change('#adjust-reason', 'Nhập lô mới');
    await act(async () =>
      document.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );
    await settle();

    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(stockText()).toBe('12');
    const firstRow = historyRows()[0]!.textContent;
    expect(firstRow).toContain('+7');
    expect(firstRow).toContain('Nhập lô mới');
    expect(historyRows()).toHaveLength(3);
    expect(document.querySelector('[role="status"]')?.textContent).toContain('+7');
    expect(document.activeElement).toBe(adjustButton());
    // Chỉ có một lần POST. Trang cập nhật bằng dữ liệu, không điều hướng hay tải lại.
    const posts = fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST');
    expect(posts).toHaveLength(1);
  });

  async function adjustBy(delta: string, reason: string) {
    const button = adjustButton()!;
    button.focus();
    await act(async () => button.click());
    await change('#adjust-delta', delta);
    await change('#adjust-reason', reason);
    await act(async () =>
      document.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(),
    );
    await settle();
  }

  it('chèn movement mới và số tồn mới ngay cả khi lần đọc lại chưa trả về', async () => {
    createServer({ hangAfterAdjust: true });
    await renderPage(writerActor);
    await adjustBy('7', 'Nhập lô mới');

    expect(stockText()).toBe('12');
    expect(historyRows()[0]!.textContent).toContain('Nhập lô mới');
    expect(historyRows()).toHaveLength(3);
    expect(document.body.textContent).toContain('3 lần thay đổi');
  });

  it('đánh dấu cũ các truy vấn liên quan và đọc lại sản phẩm, lịch sử sau khi điều chỉnh', async () => {
    const fetchMock = createServer();
    const queryClient = await renderPage(writerActor);
    // Danh sách sắp hết hàng và danh sách sản phẩm đang nằm trong cache của các trang khác.
    queryClient.setQueryData(['admin-low-stock', 1], { items: [], total: 0, page: 1, limit: 20 });
    queryClient.setQueryData(['admin-products', '', '', '', false, 1], {
      items: [],
      total: 0,
      page: 1,
      limit: 20,
    });
    const countGets = (path: string) =>
      fetchMock.mock.calls.filter(
        ([input, init]) => String(input).startsWith(path) && !init?.method,
      ).length;
    const productReadsBefore = countGets('/api/admin/products/p1');
    const movementReadsBefore = countGets('/api/admin/skus/sku-1/movements');

    await adjustBy('2', 'Nhập thêm');

    expect(queryClient.getQueryState(['admin-low-stock', 1])?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(['admin-products', '', '', '', false, 1])?.isInvalidated).toBe(
      true,
    );
    expect(countGets('/api/admin/products/p1')).toBeGreaterThan(productReadsBefore);
    expect(countGets('/api/admin/skus/sku-1/movements')).toBeGreaterThan(movementReadsBefore);
  });

  it('không tìm thấy sản phẩm thì hiện lỗi', async () => {
    createServer({ productStatus: 404 });
    await renderPage(baseActor);
    expect(document.querySelector('[role="alert"]')?.textContent).toBe('Không tìm thấy sản phẩm.');
  });

  it('SKU không thuộc sản phẩm thì hiện lỗi không tìm thấy biến thể', async () => {
    createServer({ skuId: 'sku-khac' });
    await renderPage(baseActor);
    expect(document.querySelector('[role="alert"]')?.textContent).toBe(
      'Không tìm thấy biến thể trong sản phẩm này.',
    );
  });

  it('phân trang lịch sử gọi đúng trang kế', async () => {
    const fetchMock = createServer({ total: 45 });
    await renderPage(baseActor);
    expect(document.body.textContent).toContain('Trang 1 / 3');
    const next = [...document.querySelectorAll('button')].find(
      (button) => button.textContent === 'Trang sau',
    )!;
    await act(async () => next.click());
    await settle();
    const urls = fetchMock.mock.calls.map(([input]) => String(input));
    expect(urls).toContain('/api/admin/skus/sku-1/movements?page=2&limit=20');
  });

  describe('cột STT', () => {
    const rowNumbers = () => historyRows().map((row) => row.firstElementChild!.textContent);

    it('cột đầu có tiêu đề STT, tiêu đề và ô đều có class num', async () => {
      createServer();
      await renderPage(baseActor);

      const headerCell = document.querySelector('table[aria-label="Lịch sử tồn kho"] thead th')!;
      expect(headerCell.textContent).toBe('STT');
      expect(headerCell.getAttribute('scope')).toBe('col');
      expect(headerCell.classList.contains('num')).toBe(true);
      const firstCell = historyRows()[0]!.firstElementChild!;
      expect(firstCell.tagName).toBe('TD');
      expect(firstCell.classList.contains('num')).toBe(true);
    });

    it('trang đầu đánh STT 1, 2 theo thứ tự đang hiện', async () => {
      createServer();
      await renderPage(baseActor);

      expect(rowNumbers()).toEqual(['1', '2']);
    });

    it('response page 2, limit 20 thì STT là 21 và 22', async () => {
      createServer({ responsePaging: { page: 2, limit: 20 } });
      await renderPage(baseActor);

      expect(rowNumbers()).toEqual(['21', '22']);
    });

    it('response có limit 5 ở trang 3 thì STT bắt đầu từ 11, không theo PAGE_SIZE của màn', async () => {
      createServer({ responsePaging: { page: 3, limit: 5 } });
      await renderPage(baseActor);

      expect(rowNumbers()).toEqual(['11', '12']);
    });

    it('thêm biến động thì dòng mới có STT 1 và dòng cũ dịch xuống 2, 3', async () => {
      createServer();
      await renderPage(writerActor);
      expect(rowNumbers()).toEqual(['1', '2']);

      await adjustBy('7', 'Nhập lô mới');

      expect(historyRows()[0]!.textContent).toContain('Nhập lô mới');
      expect(rowNumbers()).toEqual(['1', '2', '3']);
    });
  });
});
