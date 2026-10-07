// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../../../../src/app';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

let container: HTMLDivElement;
let root: Root;
let client: QueryClient;
let postRequests: string[];

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
  userId: 'customer-1',
  email: 'khach@example.com',
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
  postRequests = [];
  localStorage.clear();
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  client.clear();
  vi.unstubAllGlobals();
});

async function renderPage(url: string, actor: ActorData = guest) {
  // Ghi lại mọi yêu cầu ghi để kiểm tra lỗi nhập liệu không gọi API.
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method && init.method !== 'GET') postRequests.push(String(input));
      return new Response(JSON.stringify({ data: actor }));
    }),
  );
  // Đặt actor sẵn trong cache để màn tài khoản không còn ở trạng thái đang nạp khi test gõ.
  client.setQueryData(['actor'], actor);
  await act(async () => {
    root.render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[url]}>
          <App />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  });
}

function input(selector: string): HTMLInputElement {
  const element = container.querySelector(selector);
  if (!(element instanceof HTMLInputElement)) throw new Error(`Thiếu ô nhập ${selector}`);
  return element;
}

async function type(selector: string, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  await act(async () => {
    setter?.call(input(selector), value);
    input(selector).dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function submitFormOf(selector: string) {
  await act(async () => {
    input(selector).form?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  });
}

// Lỗi phải nằm ngay dưới ô sai và nối với ô bằng `aria-describedby`, để trình đọc màn hình đọc đúng ô.
function expectFieldError(selector: string, message: string) {
  const field = input(selector);
  expect(field.getAttribute('aria-invalid')).toBe('true');
  const errorId = field.getAttribute('aria-describedby');
  expect(errorId).toBeTruthy();
  const errorElement = container.querySelector(`#${errorId}`);
  expect(errorElement?.textContent).toBe(message);
  expect(field.closest('.field')?.contains(errorElement)).toBe(true);
}

function expectNoFieldError(selector: string) {
  expect(input(selector).getAttribute('aria-invalid')).toBeNull();
}

describe('lỗi nhập liệu ở màn tài khoản khách', () => {
  it('đăng nhập: lỗi email và mật khẩu hiện dưới từng ô, không gọi API', async () => {
    await renderPage('/login');
    await submitFormOf('#login-email');
    expectFieldError('#login-email', 'Email không được để trống.');
    expectFieldError('#login-password', 'Mật khẩu không được để trống.');
    // Lỗi nhập liệu không lặp lại ở thông báo chung đầu form.
    expect(container.querySelector('.auth-card > [role="alert"]')).toBeNull();

    await type('#login-email', 'khach@example.com');
    await submitFormOf('#login-email');
    expectNoFieldError('#login-email');
    expectFieldError('#login-password', 'Mật khẩu không được để trống.');
    expect(postRequests).toEqual([]);
  });

  it('đăng ký: email sai định dạng và mật khẩu ngắn hiện dưới từng ô', async () => {
    await renderPage('/register');
    await type('#reg-email', 'khach');
    await type('#reg-password', 'ngan');
    await submitFormOf('#reg-email');
    expectFieldError('#reg-email', 'Email không đúng định dạng.');
    expectFieldError('#reg-password', 'Mật khẩu phải chứa tối thiểu 8 ký tự.');
    expect(postRequests).toEqual([]);
  });

  it('quên mật khẩu: email trống hiện lỗi dưới ô email', async () => {
    await renderPage('/forgot-password');
    await submitFormOf('#forgot-email');
    expectFieldError('#forgot-email', 'Email không được để trống.');
    expect(postRequests).toEqual([]);
  });

  it('đặt lại mật khẩu: mật khẩu ngắn và xác nhận không khớp hiện dưới đúng ô', async () => {
    await renderPage('/reset-password?token=token-thu');
    await type('#reset-password', 'ngan');
    await submitFormOf('#reset-password');
    expectFieldError('#reset-password', 'Mật khẩu phải chứa tối thiểu 8 ký tự.');

    await type('#reset-password', 'matkhaumoi123');
    await type('#reset-confirm', 'matkhaukhac123');
    await submitFormOf('#reset-password');
    expectNoFieldError('#reset-password');
    expectFieldError('#reset-confirm', 'Mật khẩu xác nhận không khớp.');
    expect(postRequests).toEqual([]);
  });

  it('đổi mật khẩu trong tài khoản: xác nhận không khớp hiện dưới ô xác nhận', async () => {
    await renderPage('/me', customer);
    await type('#current-pw', 'matkhaucu123');
    await type('#new-pw', 'matkhaumoi123');
    await type('#confirm-new-pw', 'matkhaukhac123');
    await submitFormOf('#new-pw');
    expectNoFieldError('#new-pw');
    expectFieldError('#confirm-new-pw', 'Mật khẩu xác nhận không khớp.');
    expect(postRequests).toEqual([]);
  });
});
