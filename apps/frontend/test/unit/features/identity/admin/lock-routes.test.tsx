// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../../../../src/app';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

let container: HTMLDivElement;
let root: Root;
let client: QueryClient;
let writes: { method: string; url: string; body: unknown }[];

const manager: ActorData = {
  anonymousId: 'anonymous-manager',
  userId: 'user-manager',
  email: 'quanly@pickory.local',
  name: 'Quản Lý',
  kind: 'staff',
  roles: ['admin'],
  permissions: ['customers:read', 'customers:manage', 'staff:read', 'staff:manage'],
  consent: { personalization: false, marketing: false },
};

function customerRow(lockedAt: string | null) {
  return {
    id: 'customer-1',
    email: 'khach@pickory.local',
    name: 'Khách Hàng',
    phone: null,
    emailVerified: true,
    kind: 'customer',
    lockedAt,
    createdAt: '2026-10-01T00:00:00Z',
  };
}

function staffRow(lockedAt: string | null) {
  return {
    id: 'staff-1',
    email: 'nhanvien@pickory.local',
    name: 'Nhân Viên',
    phone: null,
    emailVerified: true,
    kind: 'staff',
    lockedAt,
    createdAt: '2026-10-01T00:00:00Z',
    roles: ['sales'],
  };
}

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  // Người dùng xác nhận hộp thoại hỏi lại. Test chỉ quan tâm request gửi đi sau đó.
  vi.stubGlobal('confirm', () => true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  writes = [];
  localStorage.clear();
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  client.clear();
  vi.unstubAllGlobals();
});

// Trả danh sách khách và nhân viên có đúng một dòng ở trạng thái đã chọn. Request khác `GET` được ghi lại.
function stubFetch(lockedAt: string | null) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const method = init?.method ?? 'GET';
      const url = String(input);
      if (method !== 'GET') {
        writes.push({
          method,
          url,
          body: typeof init?.body === 'string' ? JSON.parse(init.body) : null,
        });
        return new Response(JSON.stringify({ data: { success: true, message: 'ok' } }));
      }
      if (url === '/api/me') return new Response(JSON.stringify({ data: manager }));
      if (url.startsWith('/api/admin/customers')) {
        return new Response(
          JSON.stringify({
            data: { items: [customerRow(lockedAt)], total: 1, page: 1, limit: 20 },
          }),
        );
      }
      // Màn nhân viên đọc response phân trang và gửi `limit` trong query string.
      if (url.startsWith('/api/admin/staff?')) {
        return new Response(
          JSON.stringify({
            data: { items: [staffRow(lockedAt)], total: 1, page: 1, limit: 100 },
          }),
        );
      }
      return new Response(JSON.stringify({ data: [] }));
    }),
  );
}

async function renderPage(url: string) {
  await act(async () => {
    root.render(
      <StrictMode>
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={[url]}>
            <App />
          </MemoryRouter>
        </QueryClientProvider>
      </StrictMode>,
    );
  });
}

// Chờ tối đa 2 giây cho tới khi nút có nhãn xuất hiện, rồi bấm nút.
async function clickButton(label: string) {
  for (let attempt = 0; attempt < 40; attempt++) {
    const target = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === label,
    );
    if (target) {
      await act(async () => target.click());
      return;
    }
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
  }
  throw new Error(`Thiếu nút ${label}.`);
}

describe('route khóa và mở khóa tài khoản', () => {
  it('khóa khách gửi PATCH /api/admin/customers/{id} với locked true', async () => {
    stubFetch(null);
    await renderPage('/admin/customers');
    await clickButton('Khóa');

    expect(writes).toEqual([
      { method: 'PATCH', url: '/api/admin/customers/customer-1', body: { locked: true } },
    ]);
  });

  it('mở khóa khách gửi PATCH /api/admin/customers/{id} với locked false', async () => {
    stubFetch('2026-10-02T00:00:00Z');
    await renderPage('/admin/customers');
    await clickButton('Mở khóa');

    expect(writes).toEqual([
      { method: 'PATCH', url: '/api/admin/customers/customer-1', body: { locked: false } },
    ]);
  });

  it('khóa nhân viên gửi PATCH /api/admin/staff/{id} với locked true', async () => {
    stubFetch(null);
    await renderPage('/admin/staff');
    await clickButton('Khóa');

    expect(writes).toEqual([
      { method: 'PATCH', url: '/api/admin/staff/staff-1', body: { locked: true } },
    ]);
  });

  it('mở khóa nhân viên gửi PATCH /api/admin/staff/{id} với locked false', async () => {
    stubFetch('2026-10-02T00:00:00Z');
    await renderPage('/admin/staff');
    await clickButton('Mở khóa');

    expect(writes).toEqual([
      { method: 'PATCH', url: '/api/admin/staff/staff-1', body: { locked: false } },
    ]);
  });
});
