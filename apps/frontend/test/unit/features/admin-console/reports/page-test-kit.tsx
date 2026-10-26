// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { vi } from 'vitest';
import type {
  SalesReportBucket,
  SalesReportTotals,
} from '../../../../../src/features/admin-console/reports/reports-api';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

export interface ReportPageHarness {
  container: HTMLDivElement;
  requestedUrls: string[];
  location: () => string;
  searchParams: () => URLSearchParams;
}

let root: Root | null = null;
let mountedContainer: HTMLDivElement | null = null;

// Đặt đồng hồ để "tháng này" luôn là tháng 10/2026, không phụ thuộc ngày chạy test.
export function installReportClock() {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date('2026-10-15T03:00:00Z'));
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
}

export async function cleanupReportPage() {
  if (root) {
    await act(async () => root?.unmount());
    mountedContainer?.remove();
    root = null;
    mountedContainer = null;
  }
  vi.useRealTimers();
  vi.unstubAllGlobals();
}

// Hiện đường dẫn hiện tại để test kiểm URL sau mỗi thao tác.
function LocationProbe() {
  const location = useLocation();
  return <output data-location={`${location.pathname}${location.search}`} />;
}

// Dựng một trang báo cáo với actor trong cache và `fetch` giả. `respond` trả phần `data` của response.
// Trả một `Error` khi test cần một khối riêng lỗi, và lỗi đó thành response 500 như API thật.
export async function renderReportPage(
  page: ReactNode,
  initialEntry: string,
  actor: ActorData,
  respond: (url: string) => unknown,
): Promise<ReportPageHarness> {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  // Đặt actor sẵn trong cache để trang không chờ `/api/me` khi kiểm quyền.
  client.setQueryData(['actor'], actor);
  const requestedUrls: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url === '/api/me') return new Response(JSON.stringify({ data: actor }));
      requestedUrls.push(url);
      const payload = respond(url);
      // Một khối lỗi riêng trả `Error` để giao diện nhận thông báo của API, không phải lỗi mạng.
      if (payload instanceof Error) {
        return new Response(
          JSON.stringify({ error: { code: 'UNEXPECTED_ERROR', message: payload.message } }),
          { status: 500 },
        );
      }
      return new Response(JSON.stringify({ data: payload ?? [] }));
    }),
  );

  const container = document.createElement('div');
  document.body.append(container);
  mountedContainer = container;
  root = createRoot(container);
  await act(async () => {
    root?.render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[initialEntry]}>
          {page}
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  });

  const readLocation = () => container.querySelector('output')?.getAttribute('data-location') ?? '';
  return {
    container,
    requestedUrls,
    location: readLocation,
    searchParams: () => new URL(readLocation(), 'http://localhost').searchParams,
  };
}

export function textOf(container: HTMLElement): string {
  return container.textContent ?? '';
}

// Chờ tối đa 2 giây cho tới khi điều kiện đúng. Trang nạp dữ liệu qua một truy vấn bất đồng bộ.
export async function waitFor(condition: () => boolean, description: string) {
  for (let attempt = 0; attempt < 40; attempt++) {
    if (condition()) return;
    await act(async () => {
      // `Promise.withResolvers` cần lib ES2024, còn tsconfig của frontend đang đặt ES2023.
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
  }
  throw new Error(`Hết thời gian chờ: ${description}.`);
}

export async function selectOption(select: HTMLSelectElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set;
  if (!setter) throw new Error('Không đặt được giá trị ô chọn.');
  await act(async () => {
    setter.call(select, value);
    select.dispatchEvent(new Event('change', { bubbles: true }));
  });
}

// Tổng hợp rỗng của response báo cáo. Mỗi test chỉ đặt các chỉ số mà trang đang kiểm.
export function reportTotals(overrides: Partial<SalesReportTotals> = {}): SalesReportTotals {
  return {
    recognizedRevenue: 0,
    paidOrders: 0,
    soldQuantity: 0,
    paidByMethod: { cod: 0, sepay: 0 },
    cancellations: { byCustomer: 0, byShop: 0, paymentExpired: 0 },
    ...overrides,
  };
}

// Một nhóm thời gian rỗng. `key`, `from` và `to` bắt buộc vì mọi test đều đọc chúng.
export function reportBucket(
  overrides: Partial<SalesReportBucket> & { key: string; from: string; to: string },
): SalesReportBucket {
  return {
    recognizedRevenue: 0,
    paidOrders: 0,
    paidByMethod: { cod: 0, sepay: 0 },
    cancellations: { byCustomer: 0, byShop: 0, paymentExpired: 0 },
    soldQuantity: 0,
    ...overrides,
  };
}

// Nhấn một nút theo chữ hiện trên nút. Bọc trong `act` để React xử lý sự kiện rồi vẽ lại.
export async function clickButton(container: HTMLElement, name: string) {
  const button = Array.from(container.querySelectorAll('button')).find((candidate) =>
    (candidate.textContent ?? '').includes(name),
  );
  if (!button) throw new Error(`Không thấy nút ${name}.`);
  await act(async () => {
    button.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  });
}

export function findField(container: HTMLElement, label: string): HTMLSelectElement | null {
  const labelElement = Array.from(container.querySelectorAll('label')).find(
    (candidate) => candidate.textContent === label,
  );
  if (!labelElement) return null;
  return container.querySelector<HTMLSelectElement>(`[id="${labelElement.htmlFor}"]`);
}
