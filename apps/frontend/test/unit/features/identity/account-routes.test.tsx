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

// Ghi lại mọi request theo dạng `METHOD đường-dẫn`. `GET /api/me` trả actor đã chọn, `GET /api/categories` trả mảng rỗng, `GET /api/products` trả 404, request khác trả thành công.
function stubFetch(actor: ActorData) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const method = init?.method ?? 'GET';
      requests.push(`${method} ${String(input)}`);
      if (method === 'GET' && String(input) === '/api/me') {
        return new Response(JSON.stringify({ data: actor }));
      }
      // Trang tài khoản có thẻ sổ địa chỉ. Trả sổ rỗng đúng dạng mảng của API.
      if (method === 'GET' && String(input) === '/api/me/addresses') {
        return new Response(JSON.stringify({ data: [] }));
      }
      // Đăng ký xong đưa khách về trang chủ. Trang chủ cần cây danh mục dạng mảng.
      if (method === 'GET' && String(input) === '/api/categories') {
        return new Response(JSON.stringify({ data: [] }));
      }
      // Header có mini cart nên mọi trang đều đọc giỏ. Giỏ trống đúng dạng của API.
      if (method === 'GET' && String(input) === '/api/cart') {
        return new Response(
          JSON.stringify({
            data: {
              items: [],
              subtotal: 0,
              hasUnavailableItems: false,
              maxQuantityPerSku: 5,
              maxCartLines: 20,
            },
          }),
        );
      }
      // Test này không kiểm sản phẩm nên API sản phẩm trả 404.
      // Banner bỏ máy không tìm thấy và các lưới hiện lỗi, thay vì đọc dữ liệu rỗng sai dạng.
      if (method === 'GET' && String(input).startsWith('/api/products')) {
        return new Response(
          JSON.stringify({ error: { code: 'NOT_FOUND', message: 'Không tìm thấy.' } }),
          {
            status: 404,
          },
        );
      }
      return new Response(JSON.stringify({ data: { success: true, userKind: 'customer' } }), {
        status: method === 'POST' ? 201 : 200,
      });
    }),
  );
}

// Request khác `GET` của một lần thao tác. Test so khớp đúng danh sách này, nên route cũ gọi song song cũng làm test đỏ.
function writeRequests() {
  return requests.filter((request) => !request.startsWith('GET '));
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

function setInput(selector: string, value: string) {
  const input = container.querySelector(selector);
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  if (!(input instanceof HTMLInputElement) || !setter) throw new Error(`Thiếu input ${selector}.`);
  setter.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

// Chờ tối đa 2 giây cho tới khi phần tử xuất hiện. Trang tải actor bằng `GET /api/me` nên form hiện sau vài lượt render.
async function waitForElement(selector: string) {
  for (let attempt = 0; attempt < 40; attempt++) {
    if (container.querySelector(selector)) return;
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
  }
  throw new Error(`Thiếu phần tử ${selector}.`);
}

async function submitForm() {
  const form = container.querySelector('form');
  if (!form) throw new Error('Thiếu form.');
  await act(async () => {
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  });
}

describe('route vòng đời tài khoản', () => {
  it('trang đăng ký gửi POST tới /api/auth/registrations', async () => {
    stubFetch(guest);
    await renderPage('/register');
    await waitForElement('#reg-email');
    await act(async () => setInput('#reg-email', 'moi@pickory.local'));
    await act(async () => setInput('#reg-password', 'MatKhau123!'));
    await submitForm();

    // Chỉ một request ghi được gửi, và đó là route mới.
    expect(writeRequests()).toEqual(['POST /api/auth/registrations']);
  });

  it('trang xác thực email gửi POST tới /api/auth/email-verifications', async () => {
    stubFetch(guest);
    await renderPage('/verify-email?token=token-xac-thuc');
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(writeRequests()).toEqual(['POST /api/auth/email-verifications']);
  });

  it('trang quên mật khẩu gửi POST tới /api/auth/password-resets', async () => {
    stubFetch(guest);
    await renderPage('/forgot-password');
    await waitForElement('#forgot-email');
    await act(async () => setInput('#forgot-email', 'khach@pickory.local'));
    await submitForm();

    expect(writeRequests()).toEqual(['POST /api/auth/password-resets']);
  });

  it('trang đặt lại mật khẩu gửi PUT tới /api/auth/password', async () => {
    stubFetch(guest);
    await renderPage('/reset-password?token=token-dat-lai');
    await waitForElement('#reset-password');
    await act(async () => setInput('#reset-password', 'MatKhauMoi123!'));
    await act(async () => setInput('#reset-confirm', 'MatKhauMoi123!'));
    await submitForm();

    expect(writeRequests()).toEqual(['PUT /api/auth/password']);
  });

  it('form đổi mật khẩu gửi PUT tới /api/me/password', async () => {
    stubFetch(customer);
    await renderPage('/me');
    await waitForElement('#new-pw');
    await act(async () => setInput('#current-pw', 'MatKhauCu123!'));
    await act(async () => setInput('#new-pw', 'MatKhauMoi123!'));
    await act(async () => setInput('#confirm-new-pw', 'MatKhauMoi123!'));
    const form = container.querySelector('#new-pw')?.closest('form');
    if (!form) throw new Error('Thiếu form đổi mật khẩu.');
    await act(async () => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });

    expect(writeRequests()).toEqual(['PUT /api/me/password']);
  });
});
