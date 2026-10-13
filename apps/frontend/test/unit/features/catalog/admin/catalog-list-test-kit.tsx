import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, type ReactElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { vi } from 'vitest';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

// Bộ dựng chung cho test màn danh sách catalog: dựng một màn trong router, giả `fetch` và ghi lại URL đã gọi.
export interface CatalogListHarness {
  container: HTMLDivElement;
  queryClient: QueryClient;
  // Mọi URL `GET` đã gọi, theo thứ tự.
  requestedUrls: string[];
  // Chuỗi query của URL trình duyệt giả ở thời điểm hiện tại, ví dụ `?type=return`.
  currentSearch: () => string;
  cleanup: () => Promise<void>;
}

export const staffActor: ActorData = {
  anonymousId: 'anon',
  userId: 'user-1',
  kind: 'staff',
  roles: ['sales'],
  permissions: ['products:read', 'products:write', 'inventory:write'],
  consent: { personalization: false, marketing: false },
};

interface RenderOptions {
  element: ReactElement;
  // Mẫu route của màn, ví dụ `/admin/products/:productId/skus/:skuId/inventory`.
  routePath: string;
  // URL ban đầu, gồm cả query string.
  url: string;
  // Trả phần `data` của response. Trả `undefined` thì dùng mảng rỗng.
  respond: (url: URL, init?: RequestInit) => unknown;
}

// Ghi query string hiện tại ra biến của test, để assert URL mà không cần mở trình duyệt.
function LocationProbe({ onChange }: { onChange: (search: string) => void }) {
  const location = useLocation();
  onChange(location.search);
  return null;
}

export async function renderCatalogPage(options: RenderOptions): Promise<CatalogListHarness> {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const container = document.createElement('div');
  document.body.append(container);
  const root: Root = createRoot(container);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  queryClient.setQueryData(['actor'], staffActor);
  const requestedUrls: string[] = [];
  let search = '';

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost');
      if ((init?.method ?? 'GET') === 'GET') requestedUrls.push(`${url.pathname}${url.search}`);
      return new Response(JSON.stringify({ data: options.respond(url, init) ?? [] }));
    }),
  );

  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[options.url]}>
          <LocationProbe onChange={(value) => (search = value)} />
          <Routes>
            <Route path={options.routePath} element={options.element} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await settle();

  return {
    container,
    queryClient,
    requestedUrls,
    currentSearch: () => search,
    cleanup: async () => {
      await act(async () => root.unmount());
      container.remove();
      queryClient.clear();
      vi.unstubAllGlobals();
    },
  };
}

// Chờ truy vấn và render xong. React Query cần vài lượt hàng đợi vi tác vụ sau khi fetch trả về.
export async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

// Chờ khi dùng đồng hồ giả. Đẩy đồng hồ tới trước rồi cho các lời hứa đang chờ chạy hết.
export async function advance(milliseconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(milliseconds);
  });
}

// React theo dõi giá trị ô qua setter gốc. Gán trực tiếp `.value` không kích hoạt `onChange`.
export async function typeInto(input: HTMLInputElement, text: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(input, text);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

export async function selectOption(select: HTMLSelectElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(select, value);
    select.dispatchEvent(new Event('change', { bubbles: true }));
  });
}

// Tìm ô nhập qua nhãn nhìn thấy, đúng cách trình đọc màn hình nối nhãn với ô.
export function findField(container: HTMLElement, label: string) {
  const labelElement = Array.from(container.querySelectorAll('label')).find((candidate) =>
    candidate.textContent?.trim().startsWith(label),
  );
  if (!labelElement) return null;
  const target =
    labelElement.htmlFor !== ''
      ? container.querySelector<HTMLInputElement & HTMLSelectElement>(
          `[id="${labelElement.htmlFor}"]`,
        )
      : labelElement.querySelector<HTMLInputElement & HTMLSelectElement>('input');
  return target;
}

export function queryOf(url: string): Record<string, string> {
  return Object.fromEntries(new URL(url, 'http://localhost').searchParams.entries());
}
