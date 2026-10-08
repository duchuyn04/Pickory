// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProductPage } from '../../../../../src/features/catalog/customer/product-page';
import { buildProductDetail } from './product-fixtures';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.localStorage.clear();
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

function stubFetch(status: number, body: unknown) {
  const fetchMock = vi.fn(async () => Response.json(body, { status }));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

async function renderPage(url: string) {
  // `retry: false` để lỗi hiện ngay, không chờ thử lại trong test.
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[url]}>
          <Routes>
            <Route path="/p/:slug" element={<ProductPage />} />
          </Routes>
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  // Chờ fetch và truy vấn hoàn tất. Mỗi lượt nhường cho một vòng sự kiện để React Query cập nhật.
  for (let turn = 0; turn < 5; turn += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

describe('ProductPage', () => {
  it('gọi API theo slug và hiện sản phẩm', async () => {
    const fetchMock = stubFetch(200, { data: buildProductDetail() });
    await renderPage('/p/laptop-1');
    expect(fetchMock).toHaveBeenCalledWith('/api/products/laptop-1', expect.anything());
    expect(container.querySelector('h1')?.textContent).toBe('Laptop 1');
  });

  it('hiện khung tải trong lúc chờ dữ liệu', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise(() => {})),
    );
    await renderPage('/p/laptop-1');
    expect(container.querySelector('[role="status"]')?.textContent).toBe('Đang tải sản phẩm...');
    expect(container.querySelector('h1')).toBeNull();
  });

  it('hiện Không tìm thấy sản phẩm kèm link về trang chủ khi API trả PRODUCT_NOT_FOUND', async () => {
    stubFetch(404, { error: { code: 'PRODUCT_NOT_FOUND', message: 'Không tìm thấy.' } });
    await renderPage('/p/khong-co');
    expect(container.querySelector('h1')?.textContent).toBe('Không tìm thấy sản phẩm');
    expect(container.querySelector('a[href="/"]')?.textContent).toBe('Về trang chủ');
  });

  it('hiện ErrorAlert khi API trả lỗi khác', async () => {
    stubFetch(500, { error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' } });
    await renderPage('/p/laptop-1');
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Máy chủ gặp lỗi.');
  });

  it('chuyển slug cũ sang slug mới bằng replace và giữ query string', async () => {
    stubFetch(200, { data: buildProductDetail({ slug: 'laptop-moi' }) });
    await renderPage('/p/laptop-cu?sku=sku-silver-256&ref=mail');
    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe(
      '/p/laptop-moi?sku=sku-silver-256&ref=mail',
    );
  });
});
