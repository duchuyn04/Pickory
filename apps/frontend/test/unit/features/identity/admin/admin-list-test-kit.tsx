import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { vi } from 'vitest';
import { App } from '../../../../../src/app';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

// Bộ dựng chung cho test màn danh sách admin: dựng ứng dụng, giả `fetch` và ghi lại URL đã gọi.
export interface AdminListHarness {
  container: HTMLDivElement;
  // Mọi URL `GET` đã gọi, theo thứ tự.
  requestedUrls: string[];
  // Mọi request ghi (khác `GET`), kèm body đã giải mã JSON.
  writes: { method: string; url: string; body: unknown }[];
  render: (url: string) => Promise<void>;
  cleanup: () => Promise<void>;
}

export function actorWith(permissions: string[]): ActorData {
  return {
    anonymousId: 'anonymous-staff',
    userId: 'user-staff',
    email: 'nhanvien@pickory.local',
    name: 'Nhân Viên',
    kind: 'staff',
    roles: ['sales'],
    permissions,
    consent: { personalization: false, marketing: false },
  };
}

// `respond` trả phần `data` của response cho từng URL. Trả `undefined` thì dùng mảng rỗng.
export function createHarness(
  actor: ActorData,
  respond: (url: string, init?: RequestInit) => unknown,
): AdminListHarness {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const container = document.createElement('div');
  document.body.append(container);
  const root: Root = createRoot(container);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const requestedUrls: string[] = [];
  const writes: { method: string; url: string; body: unknown }[] = [];
  localStorage.clear();

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url === '/api/me') return new Response(JSON.stringify({ data: actor }));
      const method = init?.method ?? 'GET';
      if (method === 'GET') {
        requestedUrls.push(url);
      } else {
        const body = typeof init?.body === 'string' ? JSON.parse(init.body) : null;
        writes.push({ method, url, body });
      }
      // Test có thể trả một lời hứa chưa hoàn tất để giữ request đang chờ.
      const result = await respond(url, init);
      // Test cần lỗi HTTP thì trả sẵn `Response`. Bộ dựng không bọc lại.
      if (result instanceof Response) return result;
      return new Response(JSON.stringify({ data: result ?? [] }));
    }),
  );

  return {
    container,
    requestedUrls,
    writes,
    render: (url) =>
      act(async () => {
        root.render(
          <StrictMode>
            <QueryClientProvider client={client}>
              <MemoryRouter initialEntries={[url]}>
                <App />
              </MemoryRouter>
            </QueryClientProvider>
          </StrictMode>,
        );
      }),
    cleanup: async () => {
      await act(async () => root.unmount());
      container.remove();
      client.clear();
      vi.unstubAllGlobals();
    },
  };
}

// Chờ tối đa 2 giây cho tới khi điều kiện đúng. Màn nạp quyền và dữ liệu qua nhiều lượt bất đồng bộ.
export async function waitFor(condition: () => boolean, description: string) {
  for (let attempt = 0; attempt < 40; attempt++) {
    if (condition()) return;
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
  }
  throw new Error(`Hết thời gian chờ: ${description}.`);
}

// React theo dõi giá trị ô qua setter gốc. Gán trực tiếp `.value` không kích hoạt `onChange`.
export async function selectOption(select: HTMLSelectElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(select, value);
    select.dispatchEvent(new Event('change', { bubbles: true }));
  });
}

export async function clickButton(container: HTMLElement, label: string) {
  const button = findButton(container, label);
  if (!button) throw new Error(`Thiếu nút ${label}.`);
  await act(async () => button.click());
}

export function findButton(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll('button')).find(
    (button) => button.textContent?.trim() === label,
  );
}

// Tìm ô nhập qua nhãn nhìn thấy, đúng cách trình đọc màn hình nối nhãn với ô.
export function findField(container: HTMLElement, label: string) {
  const labelElement = Array.from(container.querySelectorAll('label')).find(
    (candidate) => candidate.textContent === label,
  );
  if (!labelElement) return null;
  return container.querySelector<HTMLInputElement & HTMLSelectElement>(
    `[id="${labelElement.htmlFor}"]`,
  );
}

// Chuyển URL đã gọi thành bảng tên tham số, để so sánh không phụ thuộc thứ tự.
export function queryOf(url: string): Record<string, string> {
  return Object.fromEntries(new URL(url, 'http://localhost').searchParams.entries());
}
