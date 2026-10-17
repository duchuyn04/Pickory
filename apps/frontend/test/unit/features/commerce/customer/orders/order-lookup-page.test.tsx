// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OrderLookupPage } from '../../../../../../src/features/commerce/customer/orders/order-lookup-page';
import { settle } from '../cart-test-utils';

const FIXED_SUCCESS_MESSAGE = 'Nếu thông tin đúng, link xem đơn đã được gửi tới email của đơn.';
const RATE_LIMIT_MESSAGE = 'Bạn đã tra cứu quá nhiều lần. Vui lòng thử lại sau 15 phút.';
const VALID_ORDER_CODE = 'PKAB23CD45';
const VALID_EMAIL = 'khach@example.com';

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

const acceptedReply: LookupReply = {
  status: 202,
  body: { data: { message: FIXED_SUCCESS_MESSAGE } },
};

// API giả của route tra cứu: ghi lại mọi lệnh gọi. `reply` có thể trả `Promise` để giữ request đang chạy.
function stubLookupApi(reply: () => LookupReply | Promise<LookupReply> = () => acceptedReply) {
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

async function renderPage() {
  await act(async () => {
    root.render(
      <MemoryRouter initialEntries={['/order-lookup']}>
        <OrderLookupPage />
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
  it('hiện tiêu đề và câu giải thích về link gửi qua email', async () => {
    stubLookupApi();
    await renderPage();

    expect(container.querySelector('h1')?.textContent).toBe('Tra cứu đơn hàng');
    expect(container.textContent).toContain('email đã dùng khi đặt hàng');
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

  it('hiện đúng câu cố định khi nhận 202 và không lộ mã đơn hay email đã nhập', async () => {
    stubLookupApi();
    await renderPage();

    await fillAndSubmit(VALID_ORDER_CODE, VALID_EMAIL);

    expect(container.querySelector('[role="status"]')?.textContent).toBe(FIXED_SUCCESS_MESSAGE);
    expect(container.textContent).not.toContain(VALID_ORDER_CODE);
    expect(container.textContent).not.toContain(VALID_EMAIL);
    expect(container.querySelector('form')).toBeNull();
  });

  it('cho tra cứu đơn khác và quay về form trống sau khi gửi xong', async () => {
    stubLookupApi();
    await renderPage();
    await fillAndSubmit(VALID_ORDER_CODE, VALID_EMAIL);

    const anotherLookup = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent === 'Tra cứu đơn khác',
    );
    expect(anotherLookup).toBeDefined();
    await act(async () => anotherLookup!.click());

    expect(orderCodeInput().value).toBe('');
    expect(emailInput().value).toBe('');
    expect(container.querySelector('[role="status"]')).toBeNull();
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
    expect(container.querySelector('[role="status"]')).toBeNull();
  });

  it('hiện câu thử lại sau và không hiện câu thành công khi nhận 429', async () => {
    stubLookupApi(() => ({
      status: 429,
      body: { error: { code: 'RATE_LIMITED', message: 'Thông điệp riêng của server.' } },
    }));
    await renderPage();

    await fillAndSubmit(VALID_ORDER_CODE, VALID_EMAIL);

    expect(container.querySelector('[role="alert"]')?.textContent).toBe(RATE_LIMIT_MESSAGE);
    expect(container.textContent).not.toContain(FIXED_SUCCESS_MESSAGE);
    expect(container.querySelector('[role="status"]')).toBeNull();
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
    expect(container.querySelector('[role="status"]')).toBeNull();
    expect(submitButton().disabled).toBe(false);
  });

  it('khóa nút gửi khi request đang chạy và mở lại sau khi có kết quả', async () => {
    let finishRequest: (reply: LookupReply) => void = () => {};
    const calls = stubLookupApi(
      () =>
        new Promise<LookupReply>((resolve) => {
          finishRequest = resolve;
        }),
    );
    await renderPage();

    await typeInto(orderCodeInput(), VALID_ORDER_CODE);
    await typeInto(emailInput(), VALID_EMAIL);
    await act(async () => submitButton().click());

    // Request chưa kết thúc: nút bị khóa và bấm thêm không gửi lần hai.
    expect(submitButton().disabled).toBe(true);
    await act(async () => submitButton().click());
    expect(calls).toHaveLength(1);

    await act(async () =>
      finishRequest({
        status: 429,
        body: { error: { code: 'RATE_LIMITED', message: 'Thông điệp riêng của server.' } },
      }),
    );
    await settle();

    expect(submitButton().disabled).toBe(false);
  });
});
