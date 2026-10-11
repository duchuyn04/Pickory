// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React, { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../../../../src/app';
import { InviteDialog } from '../../../../../src/features/identity/components/invite-dialog';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';

let container: HTMLDivElement;
let root: Root;
let client: QueryClient;
let postRequests: string[];
// Body của các request ghi, cùng thứ tự với `postRequests`. Test đọc body để biết form gửi trường nào.
let requestBodies: string[];

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
  requestBodies = [];
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
      if (init?.method && init.method !== 'GET') {
        postRequests.push(String(input));
        requestBodies.push(String(init.body ?? ''));
      }
      // Header có mini cart nên trang nào cũng đọc giỏ. Trả giỏ trống đúng dạng của API.
      if (String(input) === '/api/cart') {
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

  it('đăng ký: số điện thoại và tên sai hiện dưới từng ô, không gọi API', async () => {
    await renderPage('/register');
    await type('#reg-email', 'khach@example.com');
    await type('#reg-password', 'matkhau12345');
    await type('#reg-phone', 'abc');
    await type('#reg-name', 'a'.repeat(101));
    await submitFormOf('#reg-email');
    expectFieldError('#reg-phone', 'Số điện thoại không đúng định dạng.');
    expectFieldError('#reg-name', 'Họ và tên tối đa 100 ký tự.');
    expectNoFieldError('#reg-email');
    expect(postRequests).toEqual([]);

    // Sửa số về dạng hợp lệ thì lỗi của ô đó biến mất.
    await type('#reg-phone', '0912 345 678');
    await submitFormOf('#reg-email');
    expectNoFieldError('#reg-phone');
    expectFieldError('#reg-name', 'Họ và tên tối đa 100 ký tự.');
    expect(postRequests).toEqual([]);
  });

  it('tài khoản: số điện thoại sai và tên có ký tự điều khiển hiện dưới từng ô', async () => {
    await renderPage('/me', customer);
    await type('#acc-phone', '12345');
    await type('#acc-name', 'Tên\u0000x');
    await submitFormOf('#acc-name');
    expectFieldError('#acc-phone', 'Số điện thoại không đúng định dạng.');
    expectFieldError('#acc-name', 'Họ và tên chứa ký tự không hợp lệ.');
    expect(postRequests).toEqual([]);
  });

  it('tài khoản: ô để trống không báo lỗi và vẫn gửi PATCH', async () => {
    await renderPage('/me', customer);
    await type('#acc-phone', '');
    await type('#acc-name', '');
    await submitFormOf('#acc-name');
    expectNoFieldError('#acc-phone');
    expectNoFieldError('#acc-name');
    expect(postRequests).toEqual(['/api/me']);
  });

  it('mời nhân viên: tên quá dài hiện dưới ô tên, không gọi API', async () => {
    await act(async () => {
      root.render(<InviteDialog availableRoles={[]} onSuccess={() => {}} onClose={() => {}} />);
    });
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        if (init?.method && init.method !== 'GET') postRequests.push(String(input));
        return new Response(JSON.stringify({ data: {} }));
      }),
    );
    await type('#inv-email', 'nv@pickory.local');
    await type('#inv-name', 'a'.repeat(101));
    await submitFormOf('#inv-email');
    expectFieldError('#inv-name', 'Họ và tên tối đa 100 ký tự.');
    expect(postRequests).toEqual([]);

    // Ô nhập một dòng tự bỏ ký tự xuống dòng, nên test dùng ký tự chuông U+0007.
    await type('#inv-name', 'Tên\u0007x');
    await submitFormOf('#inv-email');
    expectFieldError('#inv-name', 'Họ và tên chứa ký tự không hợp lệ.');
    expect(postRequests).toEqual([]);
  });

  it('tài khoản: số cũ sai dạng không chặn việc sửa tên, và PATCH không gửi `phone`', async () => {
    await renderPage('/me', { ...customer, phone: 'abc' });
    await type('#acc-name', 'Tên Mới');
    await submitFormOf('#acc-name');
    expectNoFieldError('#acc-phone');
    expectNoFieldError('#acc-name');
    expect(postRequests).toEqual(['/api/me']);
    expect(JSON.parse(requestBodies[0] ?? '')).toEqual({ name: 'Tên Mới' });
  });

  it('tài khoản: sửa số đã lưu thành số sai dạng thì hiện lỗi và không gọi API', async () => {
    await renderPage('/me', { ...customer, phone: '0912345678' });
    await type('#acc-phone', '12345');
    await submitFormOf('#acc-phone');
    expectFieldError('#acc-phone', 'Số điện thoại không đúng định dạng.');
    expect(postRequests).toEqual([]);
  });

  it('tài khoản: xóa trống ô đã đổi gửi null, ô không đổi không có trong body', async () => {
    await renderPage('/me', { ...customer, phone: '0912345678' });
    await type('#acc-phone', '');
    await submitFormOf('#acc-phone');
    expect(JSON.parse(requestBodies[0] ?? '')).toEqual({ phone: null });
  });

  it('tài khoản: không đổi ô nào vẫn gửi PATCH với body rỗng', async () => {
    await renderPage('/me', { ...customer, phone: '0912345678' });
    await submitFormOf('#acc-name');
    expect(postRequests).toEqual(['/api/me']);
    expect(JSON.parse(requestBodies[0] ?? '')).toEqual({});
  });
});
