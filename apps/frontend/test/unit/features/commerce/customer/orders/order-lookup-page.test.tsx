// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OrderLookupPage } from '../../../../../../src/features/commerce/customer/orders/order-lookup-page';
import { settle } from '../cart-test-utils';

const RATE_LIMIT_MESSAGE = 'Bạn đã tra cứu quá nhiều lần. Vui lòng thử lại sau 15 phút.';
const NOT_FOUND_MESSAGE = 'Không tìm thấy đơn hàng với mã và email này.';
const VALID_ORDER_CODE = 'PKAB23CD45';
const VALID_EMAIL = 'khach@example.com';
const SUCCESS_TOKEN = 'token-abc';

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

interface LookupCall {
  url: string;
  method: string;
  body: unknown;
}

interface LookupReply {
  status: number;
  body: unknown;
}

const successReply: LookupReply = {
  status: 200,
  body: { data: { code: VALID_ORDER_CODE, accessToken: SUCCESS_TOKEN } },
};

// API giả của route tra cứu: ghi lại mọi lệnh gọi. `reply` có thể trả `Promise` để giữ request đang chạy.
function stubLookupApi(reply: () => LookupReply | Promise<LookupReply> = () => successReply) {
  const calls: LookupCall[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push({
      url: String(input),
      method: init?.method ?? 'GET',
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : null,
    });
    const result = await reply();
    return new Response(JSON.stringify(result.body), {
      status: result.status,
      headers: { 'Content-Type': 'application/json' },
    });
  });
  vi.stubGlobal('fetch', fetchMock);
  return calls;
}

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location">{`${location.pathname}${location.search}`}</div>;
}

// TypeScript của repo chưa có `Promise.withResolvers`, nên giữ một helper nhỏ cho test.
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

async function renderPage() {
  await act(async () => {
    root.render(
      <MemoryRouter initialEntries={['/order-lookup']}>
        <Routes>
          <Route path="/order-lookup" element={<OrderLookupPage />} />
          <Route path="*" element={<LocationProbe />} />
        </Routes>
      </MemoryRouter>,
    );
  });
  await settle();
}

// Gõ vào ô bằng setter gốc của `HTMLInputElement`, để React nhận sự kiện `input`.
async function typeInto(input: HTMLInputElement, value: string) {
  const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  await act(async () => {
    valueSetter.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

function orderCodeInput() {
  return container.querySelector('#order-lookup-code') as HTMLInputElement;
}

function emailInput() {
  return container.querySelector('#order-lookup-email') as HTMLInputElement;
}

function submitButton() {
  return container.querySelector('button[type="submit"]') as HTMLButtonElement;
}

async function fillAndSubmit(orderCode: string, email: string) {
  await typeInto(orderCodeInput(), orderCode);
  await typeInto(emailInput(), email);
  await act(async () => submitButton().click());
  await settle();
}

describe('OrderLookupPage', () => {
  it('hiện tiêu đề và hướng dẫn xem đơn ngay trên trang', async () => {
    stubLookupApi();
    await renderPage();

    expect(container.querySelector('h1')?.textContent).toBe('Tra cứu đơn hàng');
    expect(container.textContent).toContain('Nhập mã đơn và email đặt hàng để xem đơn ngay.');
  });

  it('hiện lỗi đúng ô và không gọi API khi form sai định dạng', async () => {
    const calls = stubLookupApi();
    await renderPage();

    await fillAndSubmit('PK123', 'khong-phai-email');

    expect(container.querySelector('#order-lookup-code-error')?.textContent).toBe(
      'Mã đơn hàng không đúng định dạng.',
    );
    expect(container.querySelector('#order-lookup-email-error')?.textContent).toBe(
      'Email không đúng định dạng.',
    );
    expect(orderCodeInput().getAttribute('aria-invalid')).toBe('true');
    expect(orderCodeInput().getAttribute('aria-describedby')).toBe('order-lookup-code-error');
    expect(emailInput().getAttribute('aria-invalid')).toBe('true');
    expect(emailInput().getAttribute('aria-describedby')).toBe('order-lookup-email-error');
    expect(calls).toHaveLength(0);
  });

  it('báo cần nhập khi bỏ trống cả hai ô', async () => {
    const calls = stubLookupApi();
    await renderPage();

    await fillAndSubmit('', '');

    expect(container.querySelector('#order-lookup-code-error')?.textContent).toBe(
      'Cần nhập mã đơn hàng.',
    );
    expect(container.querySelector('#order-lookup-email-error')?.textContent).toBe(
      'Cần nhập email.',
    );
    expect(calls).toHaveLength(0);
  });

  it('gửi mã đơn đã đổi sang chữ hoa và email đã cắt khoảng trắng', async () => {
    const calls = stubLookupApi();
    await renderPage();

    await fillAndSubmit('  pkab23cd45 ', '  khach@example.com  ');

    expect(calls).toHaveLength(1);
    expect(calls[0]).toEqual({
      url: '/api/order-lookups',
      method: 'POST',
      body: { orderCode: VALID_ORDER_CODE, email: VALID_EMAIL },
    });
  });

  it('khớp thông tin thì chuyển tới trang chi tiết đơn với token', async () => {
    stubLookupApi();
    await renderPage();

    await fillAndSubmit(VALID_ORDER_CODE, VALID_EMAIL);

    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe(
      `/orders/${VALID_ORDER_CODE}?t=${SUCCESS_TOKEN}`,
    );
  });

  it('hiện lỗi chung khi không tìm thấy đơn và giữ dữ liệu đã nhập', async () => {
    stubLookupApi(() => ({
      status: 404,
      body: {
        error: {
          code: 'ORDER_NOT_FOUND',
          message: 'Không tìm thấy đơn hàng với mã và email này.',
        },
      },
    }));
    await renderPage();

    await fillAndSubmit(VALID_ORDER_CODE, VALID_EMAIL);

    expect(container.querySelector('[role="alert"]')?.textContent).toBe(NOT_FOUND_MESSAGE);
    expect(orderCodeInput().value).toBe(VALID_ORDER_CODE);
    expect(emailInput().value).toBe(VALID_EMAIL);
    expect(submitButton().disabled).toBe(false);
  });

  it('gắn lỗi từ server vào đúng ô theo details key', async () => {
    stubLookupApi(() => ({
      status: 400,
      body: {
        error: {
          code: 'VALIDATION_ERROR',
          message: 'Dữ liệu không hợp lệ.',
          details: [
            { key: 'orderCode', message: 'Mã đơn hàng không đúng định dạng.' },
            { key: 'email', message: 'Email không đúng định dạng.' },
          ],
        },
      },
    }));
    await renderPage();

    await fillAndSubmit(VALID_ORDER_CODE, VALID_EMAIL);

    expect(container.querySelector('#order-lookup-code-error')?.textContent).toBe(
      'Mã đơn hàng không đúng định dạng.',
    );
    expect(container.querySelector('#order-lookup-email-error')?.textContent).toBe(
      'Email không đúng định dạng.',
    );
    expect(container.querySelector('[data-testid="location"]')).toBeNull();
  });

  it('hiện câu thử lại sau và không chuyển trang khi nhận 429', async () => {
    stubLookupApi(() => ({
      status: 429,
      body: { error: { code: 'RATE_LIMITED', message: 'Thông điệp riêng của server.' } },
    }));
    await renderPage();

    await fillAndSubmit(VALID_ORDER_CODE, VALID_EMAIL);

    expect(container.querySelector('[role="alert"]')?.textContent).toBe(RATE_LIMIT_MESSAGE);
    expect(container.querySelector('[data-testid="location"]')).toBeNull();
    // Nút mở lại để khách thử lần sau.
    expect(submitButton().disabled).toBe(false);
  });

  it('hiện lỗi chung khi máy chủ lỗi và mở lại nút gửi', async () => {
    stubLookupApi(() => ({
      status: 500,
      body: { error: { code: 'INTERNAL_ERROR', message: 'Đã có lỗi xảy ra.' } },
    }));
    await renderPage();

    await fillAndSubmit(VALID_ORDER_CODE, VALID_EMAIL);

    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Đã có lỗi xảy ra.');
    expect(submitButton().disabled).toBe(false);
  });

  it('khóa nút gửi khi request đang chạy và mở lại sau khi có kết quả', async () => {
    const reply = deferred<LookupReply>();
    const calls = stubLookupApi(() => reply.promise);
    await renderPage();

    await typeInto(orderCodeInput(), VALID_ORDER_CODE);
    await typeInto(emailInput(), VALID_EMAIL);
    await act(async () => submitButton().click());

    // Request chưa kết thúc: nút bị khóa và bấm thêm không gửi lần hai.
    expect(submitButton().disabled).toBe(true);
    await act(async () => submitButton().click());
    expect(calls).toHaveLength(1);

    await act(async () =>
      reply.resolve({
        status: 429,
        body: { error: { code: 'RATE_LIMITED', message: 'Thông điệp riêng của server.' } },
      }),
    );
    await settle();

    expect(submitButton().disabled).toBe(false);
  });
});
