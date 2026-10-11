// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../../../src/app';
import type { ActorData } from '../../../../src/features/identity/hooks/use-actor';

let container: HTMLDivElement;
let root: Root;
let client: QueryClient;
let requests: string[];

const guest: ActorData = {
  anonymousId: 'test-anonymous',
  userId: null,
  kind: 'guest',
  roles: [],
  permissions: [],
  consent: { personalization: false, marketing: false },
};

const customer: ActorData = {
  ...guest,
  userId: 'user-customer',
  email: 'khach@pickory.local',
  name: 'Khách Hàng',
  kind: 'customer',
  roles: ['customer'],
};

const staff: ActorData = {
  ...guest,
  userId: 'user-staff',
  email: 'admin@pickory.local',
  name: 'Quản Trị',
  kind: 'staff',
  roles: ['admin'],
};

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  requests = [];
  localStorage.clear();
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  client.clear();
  vi.unstubAllGlobals();
});

// Ghi lại mọi request theo dạng `METHOD đường-dẫn`. `GET /api/me` trả actor đã chọn, `GET /api/categories` trả mảng rỗng, request khác trả `{ data: {} }`.
function stubFetch(actor: ActorData) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const method = init?.method ?? 'GET';
      requests.push(`${method} ${String(input)}`);
      if (method === 'GET' && String(input) === '/api/me') {
        return new Response(JSON.stringify({ data: actor }));
      }
      // Đăng nhập và đăng xuất đưa khách về trang chủ. Trang chủ cần cây danh mục dạng mảng.
      if (method === 'GET' && String(input) === '/api/categories') {
        return new Response(JSON.stringify({ data: [] }));
      }
      return new Response(JSON.stringify({ data: {} }), { status: method === 'POST' ? 201 : 200 });
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

// Request khác `GET` của một lần thao tác. Test so khớp đúng danh sách này.
function writeRequests() {
  return requests.filter((request) => !request.startsWith('GET '));
}

function setInput(selector: string, value: string) {
  const input = container.querySelector(selector);
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  if (!(input instanceof HTMLInputElement) || !setter) throw new Error(`Thiếu input ${selector}.`);
  setter.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

// Nút xuất hiện sau khi `GET /api/me` trả actor, nên hàm chờ tối đa 2 giây. Nút khu quản trị chỉ có `aria-label`.
async function clickButton(label: string) {
  for (let attempt = 0; attempt < 40; attempt++) {
    const target = Array.from(container.querySelectorAll('button')).find(
      (button) =>
        button.textContent?.trim() === label || button.getAttribute('aria-label') === label,
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

describe('route phiên đăng nhập', () => {
  it('trang đăng nhập khách gửi POST tới /api/auth/sessions', async () => {
    stubFetch(guest);
    await renderPage('/login');
    await act(async () => setInput('#login-email', 'khach@pickory.local'));
    await act(async () => setInput('#login-password', 'MatKhau123!'));
    await clickButton('Đăng nhập');

    // Chỉ một request ghi được gửi, và đó là route mới. Route cũ không bị gọi song song.
    expect(writeRequests()).toEqual(['POST /api/auth/sessions']);
  });

  it('trang đăng nhập nhân viên gửi POST tới /api/admin/auth/sessions', async () => {
    stubFetch(guest);
    await renderPage('/admin/login');
    await act(async () => setInput('#email', 'admin@pickory.local'));
    await act(async () => setInput('#password', 'MatKhau123!'));
    await clickButton('Đăng nhập');

    expect(writeRequests()).toEqual(['POST /api/admin/auth/sessions']);
  });

  it('nút đăng xuất ở header gửi DELETE tới /api/auth/sessions/current', async () => {
    stubFetch(customer);
    await renderPage('/privacy');
    await clickButton('Đăng xuất');

    expect(writeRequests()).toEqual(['DELETE /api/auth/sessions/current']);
  });

  it('nút đăng xuất của khu quản trị gửi DELETE tới /api/auth/sessions/current', async () => {
    stubFetch(staff);
    await renderPage('/admin');
    await clickButton('Đăng xuất');

    expect(writeRequests()).toEqual(['DELETE /api/auth/sessions/current']);
  });
});
