// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Link, MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProductDetail } from '../../../../../src/features/catalog/customer/catalog-api';
import { ProductPage } from '../../../../../src/features/catalog/customer/product-page';
import { ApiError } from '../../../../../src/shared/api';
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

// Link nội bộ để test đổi URL như khách bấm, mà không cần dựng cả bố cục trang.
function TestLinks() {
  return (
    <nav>
      <Link to="/p/laptop-1?sku=sku-silver-256" data-testid="same-product-other-sku">
        Cùng sản phẩm, SKU khác
      </Link>
      <Link to="/p/laptop-2" data-testid="other-product">
        Sản phẩm khác
      </Link>
      <Link to="/p/laptop-1" data-testid="first-product">
        Sản phẩm đầu
      </Link>
    </nav>
  );
}

// Cách `/api/events` phản hồi trong test: thành công, lỗi 500 hoặc mất mạng.
type EventsBehavior = 'ok' | 'server-error' | 'network-error';

function respondToEvents(behavior: EventsBehavior): Response {
  if (behavior === 'network-error') throw new TypeError('Failed to fetch');
  if (behavior === 'server-error') {
    return Response.json(
      { error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' } },
      { status: 500 },
    );
  }
  return new Response(null, { status: 204 });
}

const emptyReviews = {
  data: {
    summary: { average: null, count: 0, distribution: {} },
    items: [],
    nextCursor: null,
  },
};

function stubFetch(status: number, body: unknown, eventsBehavior: EventsBehavior = 'ok') {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    // Route lượt xem trả 204 như API thật, hoặc lỗi theo từng ca để kiểm trang vẫn dùng được.
    if (String(input) === '/api/events') return respondToEvents(eventsBehavior);
    // Khối đánh giá của trang gọi thêm route đánh giá. Test này chỉ kiểm sản phẩm, nên route đó trả danh sách rỗng.
    return String(input).includes('/reviews')
      ? Response.json(emptyReviews)
      : Response.json(body, { status });
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

// Mỗi slug trong URL nhận sản phẩm mang đúng slug đó. Test chuyển sản phẩm cần API trả sản phẩm khớp slug.
function stubFetchBySlug() {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input);
    if (path === '/api/events') return respondToEvents('ok');
    if (path.includes('/reviews')) return Response.json(emptyReviews);
    const slug = decodeURIComponent(path.split('/api/products/')[1] ?? '');
    return Response.json({ data: buildProductDetail({ slug, name: `Sản phẩm ${slug}` }) });
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

// Đếm số lần trang gọi route lượt xem, bất kể các lần gọi API khác.
function countEventCalls(fetchMock: ReturnType<typeof vi.fn>): number {
  return fetchMock.mock.calls.filter(([input]) => String(input) === '/api/events').length;
}

// Chờ fetch và truy vấn hoàn tất. Mỗi lượt nhường cho một vòng sự kiện để React Query cập nhật.
async function settle() {
  for (let turn = 0; turn < 5; turn += 1) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }
}

async function clickLink(testId: string) {
  const link = container.querySelector<HTMLAnchorElement>(`[data-testid="${testId}"]`);
  if (!link) throw new Error(`Không thấy link ${testId}`);
  await act(async () => link.click());
  await settle();
}

async function renderPage(
  url: string,
  options: { strict?: boolean; cachedProduct?: ProductDetail; cachedProductError?: ApiError } = {},
) {
  // `retry: false` để lỗi hiện ngay, không chờ thử lại trong test.
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  // Sản phẩm có sẵn trong cache thì trang hiện ngay lúc mount, giống khi khách quay lại sản phẩm đã xem.
  if (options.cachedProduct) {
    const queryKey = ['product', options.cachedProduct.slug];
    queryClient.setQueryData(queryKey, options.cachedProduct);
    // Query giữ `data` cũ nhưng đang ở trạng thái lỗi, như khi lần tải lại trước đó nhận 404.
    if (options.cachedProductError) {
      queryClient
        .getQueryCache()
        .find({ queryKey })
        ?.setState({ status: 'error', error: options.cachedProductError });
    }
  }
  const page = (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path="/p/:slug" element={<ProductPage />} />
        </Routes>
        <TestLinks />
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>
  );
  await act(async () => root.render(options.strict ? <StrictMode>{page}</StrictMode> : page));
  await settle();
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

  describe('lượt xem sản phẩm', () => {
    it('mở sản phẩm gửi đúng 1 request /api/events', async () => {
      const fetchMock = stubFetch(200, { data: buildProductDetail() });
      await renderPage('/p/laptop-1');
      expect(countEventCalls(fetchMock)).toBe(1);
    });

    it('gửi đúng 1 request khi bọc trong StrictMode', async () => {
      const fetchMock = stubFetch(200, { data: buildProductDetail() });
      await renderPage('/p/laptop-1', { strict: true });
      expect(container.querySelector('h1')?.textContent).toBe('Laptop 1');
      expect(countEventCalls(fetchMock)).toBe(1);
    });

    it('sản phẩm có sẵn trong cache và bọc StrictMode vẫn gửi đúng 1 request', async () => {
      // Dữ liệu có sẵn lúc mount nên effect chạy ngay, và StrictMode chạy nó lần thứ hai. Cờ `useRef` chặn lần đó.
      const fetchMock = stubFetch(200, { data: buildProductDetail() });
      await renderPage('/p/laptop-1', { strict: true, cachedProduct: buildProductDetail() });
      expect(container.querySelector('h1')?.textContent).toBe('Laptop 1');
      expect(countEventCalls(fetchMock)).toBe(1);
    });

    it('query còn data cũ nhưng đang ở trạng thái lỗi 404 thì hiện Không tìm thấy và không gửi request', async () => {
      // Lần tải lại trước nhận 404 nên query giữ `data` cũ kèm lỗi. Trang hiện lỗi, nên không phải lượt xem.
      const fetchMock = stubFetch(404, {
        error: { code: 'PRODUCT_NOT_FOUND', message: 'Không tìm thấy.' },
      });
      await renderPage('/p/laptop-1', {
        cachedProduct: buildProductDetail(),
        cachedProductError: new ApiError('PRODUCT_NOT_FOUND', 404, 'Không tìm thấy.'),
      });
      expect(container.querySelector('h1')?.textContent).toBe('Không tìm thấy sản phẩm');
      expect(countEventCalls(fetchMock)).toBe(0);
    });

    it('đổi ?sku= trên cùng slug không gửi thêm request', async () => {
      const fetchMock = stubFetchBySlug();
      await renderPage('/p/laptop-1');
      await clickLink('same-product-other-sku');
      expect(container.querySelector('[data-testid="location"]')?.textContent).toBe(
        '/p/laptop-1?sku=sku-silver-256',
      );
      expect(countEventCalls(fetchMock)).toBe(1);
    });

    it('sang sản phẩm khác gửi thêm đúng 1 request', async () => {
      const fetchMock = stubFetchBySlug();
      await renderPage('/p/laptop-1');
      await clickLink('other-product');
      expect(container.querySelector('h1')?.textContent).toBe('Sản phẩm laptop-2');
      expect(countEventCalls(fetchMock)).toBe(2);
    });

    it('mở lại sản phẩm đầu bằng điều hướng mới tính thêm một lượt', async () => {
      const fetchMock = stubFetchBySlug();
      await renderPage('/p/laptop-1');
      await clickLink('other-product');
      await clickLink('first-product');
      expect(countEventCalls(fetchMock)).toBe(3);
    });

    it('API sản phẩm trả 404 PRODUCT_NOT_FOUND thì không gửi request', async () => {
      const fetchMock = stubFetch(404, {
        error: { code: 'PRODUCT_NOT_FOUND', message: 'Không tìm thấy.' },
      });
      await renderPage('/p/khong-co');
      expect(container.querySelector('h1')?.textContent).toBe('Không tìm thấy sản phẩm');
      expect(countEventCalls(fetchMock)).toBe(0);
    });

    it('API sản phẩm trả 500 thì không gửi request', async () => {
      const fetchMock = stubFetch(500, {
        error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' },
      });
      await renderPage('/p/laptop-1');
      expect(container.querySelector('[role="alert"]')).not.toBeNull();
      expect(countEventCalls(fetchMock)).toBe(0);
    });

    it('slug cũ chỉ gửi 1 request, từ trang của slug mới', async () => {
      const fetchMock = stubFetch(200, { data: buildProductDetail({ slug: 'laptop-moi' }) });
      await renderPage('/p/laptop-cu');
      expect(container.querySelector('[data-testid="location"]')?.textContent).toBe(
        '/p/laptop-moi',
      );
      expect(container.querySelector('h1')?.textContent).toBe('Laptop 1');
      expect(countEventCalls(fetchMock)).toBe(1);
    });

    it.each<EventsBehavior>(['server-error', 'network-error'])(
      '/api/events lỗi (%s) thì trang vẫn hiện sản phẩm và không có thông báo lỗi',
      async (behavior) => {
        const fetchMock = stubFetch(200, { data: buildProductDetail() }, behavior);
        await renderPage('/p/laptop-1');
        expect(countEventCalls(fetchMock)).toBe(1);
        expect(container.querySelector('h1')?.textContent).toBe('Laptop 1');
        expect(container.querySelector('[role="alert"]')).toBeNull();
      },
    );
  });
});
