// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { InventoryPage } from '../../../../../src/features/catalog/admin/inventory-page';
import type { LowStockSku } from '../../../../../src/features/catalog/admin/inventory-logic';

let container: HTMLDivElement;
let root: Root;

const items: LowStockSku[] = [
  {
    skuId: 'sku-a',
    code: 'AIR-DO-01',
    productId: 'prod-1',
    productName: 'Điện thoại Air',
    optionValues: { Màu: 'Đỏ', 'Bộ nhớ': '128GB' },
    stock: 0,
  },
  {
    skuId: 'sku-b',
    code: 'AIR-XANH-01',
    productId: 'prod-1',
    productName: 'Điện thoại Air',
    optionValues: {},
    stock: 5,
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

async function renderPage(fetchMock: ReturnType<typeof vi.fn>) {
  vi.stubGlobal('fetch', fetchMock);
  await act(async () =>
    root.render(
      <QueryClientProvider
        client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
      >
        <MemoryRouter>
          <InventoryPage />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();
}

describe('InventoryPage', () => {
  it('hiện dòng đúng dữ liệu API kèm liên kết tới trang SKU', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse({ data: { items, total: 2, page: 1, limit: 20 } }));
    await renderPage(fetchMock);

    expect(fetchMock.mock.calls[0]![0]).toBe('/api/admin/inventory/low-stock?page=1&limit=20');
    const rows = [...document.querySelectorAll('tbody tr')];
    expect(rows).toHaveLength(2);
    expect(rows[0]!.textContent).toContain('AIR-DO-01');
    expect(rows[0]!.textContent).toContain('Điện thoại Air');
    expect(rows[0]!.textContent).toContain('Đỏ / 128GB');
    expect(rows[0]!.textContent).toContain('Hết hàng');
    expect(rows[1]!.textContent).toContain('Mặc định');
    expect(rows[1]!.textContent).toContain('Sắp hết');
    const link = rows[0]!.querySelector('a')!;
    expect(link.getAttribute('href')).toBe('/admin/products/prod-1/skus/sku-a/inventory');
    expect(document.body.textContent).toContain('2 SKU');
  });

  it('hiện trạng thái rỗng khi không có SKU sắp hết hàng', async () => {
    await renderPage(
      vi
        .fn()
        .mockResolvedValue(jsonResponse({ data: { items: [], total: 0, page: 1, limit: 20 } })),
    );
    expect(document.querySelector('.empty-state')?.textContent).toBe(
      'Không có SKU nào sắp hết hàng.',
    );
    expect(document.querySelector('table')).toBeNull();
  });

  it('hiện thông báo lỗi khi API thất bại', async () => {
    await renderPage(
      vi
        .fn()
        .mockResolvedValue(
          jsonResponse({ error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' } }, 500),
        ),
    );
    expect(document.querySelector('[role="alert"]')?.textContent).toBe('Máy chủ gặp lỗi.');
  });

  it('phân trang gọi đúng trang kế và khóa nút ở trang cuối', async () => {
    const fetchMock = vi.fn().mockImplementation(async (input: string) => {
      const page = Number(new URL(input, 'http://localhost').searchParams.get('page'));
      return jsonResponse({ data: { items, total: 45, page, limit: 20 } });
    });
    await renderPage(fetchMock);
    expect(document.body.textContent).toContain('Trang 1 / 3');

    const button = (name: string) =>
      [...document.querySelectorAll('button')].find((item) => item.textContent === name)!;
    expect(button('Trang trước').disabled).toBe(true);
    await act(async () => button('Trang sau').click());
    await settle();
    expect(fetchMock.mock.calls.map(([input]) => input)).toContain(
      '/api/admin/inventory/low-stock?page=2&limit=20',
    );
    await act(async () => button('Trang sau').click());
    await settle();
    expect(document.body.textContent).toContain('Trang 3 / 3');
    expect(button('Trang sau').disabled).toBe(true);
  });

  describe('cột STT', () => {
    function rowNumbers() {
      return [...document.querySelectorAll('tbody tr')].map(
        (row) => row.firstElementChild!.textContent,
      );
    }

    async function renderPaged(paging: { page: number; limit: number }) {
      const fetchMock = vi
        .fn()
        .mockResolvedValue(jsonResponse({ data: { items, total: 100, ...paging } }));
      await renderPage(fetchMock);
    }

    it('cột đầu có tiêu đề STT, tiêu đề và ô đều có class num', async () => {
      await renderPaged({ page: 1, limit: 20 });

      const headerCell = document.querySelector('thead th')!;
      expect(headerCell.textContent).toBe('STT');
      expect(headerCell.getAttribute('scope')).toBe('col');
      expect(headerCell.classList.contains('num')).toBe(true);
      const firstCell = document.querySelector('tbody tr')!.firstElementChild!;
      expect(firstCell.tagName).toBe('TD');
      expect(firstCell.classList.contains('num')).toBe(true);
      expect(document.querySelector('tbody tr th[scope="row"]')).not.toBeNull();
    });

    it('response page 2, limit 20 thì STT là 21 và 22', async () => {
      await renderPaged({ page: 2, limit: 20 });

      expect(rowNumbers()).toEqual(['21', '22']);
    });

    it('response có limit 5 ở trang 3 thì STT bắt đầu từ 11, không theo PAGE_SIZE của màn', async () => {
      await renderPaged({ page: 3, limit: 5 });

      expect(rowNumbers()).toEqual(['11', '12']);
    });
  });
});
