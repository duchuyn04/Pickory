// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProductListResponse } from '../../../../../src/features/catalog/customer/catalog-api';
import { useProductList } from '../../../../../src/features/catalog/customer/use-product-list';

interface PendingRequest {
  path: string;
  signal: AbortSignal;
  resolve: (response: Response) => void;
  reject: (error: unknown) => void;
}

let container: HTMLDivElement;
let root: Root;
let pendingRequests: PendingRequest[];

function listResponse(total: number): ProductListResponse {
  return {
    items: [],
    page: 1,
    pageSize: 24,
    total,
    facets: { brands: [], attributes: [] },
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function Fixture({ apiPath }: { apiPath: string }) {
  const state = useProductList(apiPath);
  return (
    <output>
      {state.status}:{state.data?.total ?? 'none'}
      {state.status === 'error' ? `:${state.error.code}` : ''}
    </output>
  );
}

function shownState(): string {
  return container.querySelector('output')?.textContent ?? '';
}

async function renderPath(apiPath: string) {
  await act(async () => root.render(<Fixture apiPath={apiPath} />));
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  pendingRequests = [];
  // Mỗi lần gọi `fetch` chờ test tự trả lời, để test quyết định thứ tự response về.
  vi.stubGlobal(
    'fetch',
    vi.fn(
      (path: string, init?: RequestInit) =>
        new Promise<Response>((resolve, reject) => {
          pendingRequests.push({ path, signal: init!.signal!, resolve, reject });
        }),
    ),
  );
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe('useProductList', () => {
  it('báo đang tải rồi trả dữ liệu khi request xong', async () => {
    await renderPath('/api/products?category=laptop');
    expect(shownState()).toBe('loading:none');

    await act(async () => pendingRequests[0]!.resolve(jsonResponse({ data: listResponse(5) })));

    expect(shownState()).toBe('success:5');
  });

  it('hủy request cũ khi đường dẫn đổi', async () => {
    await renderPath('/api/products?category=laptop');
    await renderPath('/api/products?category=laptop&brand=asus');

    expect(pendingRequests[0]!.signal.aborted).toBe(true);
    expect(pendingRequests[1]!.signal.aborted).toBe(false);
  });

  it('response cũ về muộn không ghi đè kết quả mới', async () => {
    await renderPath('/api/products?category=laptop');
    await renderPath('/api/products?category=laptop&brand=asus');

    // Request mới trả trước, request cũ trả sau, như khi mạng chậm lúc khách đổi bộ lọc nhanh.
    await act(async () => pendingRequests[1]!.resolve(jsonResponse({ data: listResponse(2) })));
    await act(async () => pendingRequests[0]!.resolve(jsonResponse({ data: listResponse(99) })));

    expect(shownState()).toBe('success:2');
  });

  it('lỗi của request cũ không thay kết quả mới', async () => {
    await renderPath('/api/products?category=laptop');
    await renderPath('/api/products?category=laptop&brand=asus');

    await act(async () => pendingRequests[1]!.resolve(jsonResponse({ data: listResponse(2) })));
    await act(async () => pendingRequests[0]!.reject(new DOMException('aborted', 'AbortError')));

    expect(shownState()).toBe('success:2');
  });

  it('giữ kết quả trước trong lúc tải bộ lọc mới để lưới không nháy trắng', async () => {
    await renderPath('/api/products?category=laptop');
    await act(async () => pendingRequests[0]!.resolve(jsonResponse({ data: listResponse(5) })));

    await renderPath('/api/products?category=laptop&brand=asus');

    expect(shownState()).toBe('loading:5');
  });

  it('trả lỗi API kèm mã lỗi', async () => {
    await renderPath('/api/products?category=laptop&attr.x=1');

    await act(async () =>
      pendingRequests[0]!.resolve(
        jsonResponse({ error: { code: 'UNKNOWN_FILTER', message: 'Bộ lọc không tồn tại.' } }, 400),
      ),
    );

    expect(shownState()).toBe('error:none:UNKNOWN_FILTER');
  });
});
