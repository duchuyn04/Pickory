// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CheckoutPage } from '../../../../../../src/features/commerce/customer/checkout/checkout-page';
import { OrderSuccessPage } from '../../../../../../src/features/commerce/customer/checkout/order-success-page';
import { PREVIEW_DEBOUNCE_MS } from '../../../../../../src/features/commerce/customer/checkout/use-checkout-preview';
import { buildCart, buildCartLine } from '../cart-fixtures';
import {
  renderWithProviders,
  settle,
  stubCartApi,
  type FakeApiReply,
  type RecordedCall,
} from '../cart-test-utils';

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

const provinces = [{ code: '201', name: 'Hà Nội' }];
const wards = [{ code: '1A0101', name: 'Phường Ba Đình' }];

function buildPreview(overrides: Record<string, unknown> = {}) {
  return {
    subtotal: 20_000_000,
    shippingFee: 30_000,
    shippingDiscount: 0,
    customerShippingFee: 30_000,
    total: 20_030_000,
    freeShippingThreshold: 50_000_000,
    codMaxAmount: 30_000_000,
    codAllowed: true,
    expectedDeliveryDate: null,
    ...overrides,
  };
}

function okReply(data: unknown, status = 200): FakeApiReply {
  return { status, body: { data } };
}

function errorReply(
  status: number,
  code: string,
  message: string,
  details?: { key: string; message: string }[],
): FakeApiReply {
  return { status, body: { error: { code, message, details } } };
}

interface CheckoutServerOptions {
  previewReply?: (call: RecordedCall) => FakeApiReply;
  orderReply?: (call: RecordedCall) => FakeApiReply | Promise<FakeApiReply>;
}

// API giả cho trang đặt hàng. Giỏ, danh mục địa chỉ, preview và đặt đơn đều do test điều khiển.
function stubCheckoutApi(options: CheckoutServerOptions = {}, cart = buildCart([buildCartLine()])) {
  const previewReply = options.previewReply ?? (() => okReply(buildPreview()));
  const orderReply =
    options.orderReply ??
    (() =>
      okReply(
        {
          code: 'DH-0001',
          status: 'pending',
          paymentStatus: 'unpaid',
          paymentMethod: 'cod',
          subtotal: 20_000_000,
          shippingFee: 30_000,
          shippingDiscount: 0,
          total: 20_030_000,
          paymentDeadline: null,
          accessToken: 'token-abc',
        },
        201,
      ));

  const api = stubCartApi(cart, (call) => {
    if (call.path === '/api/addresses/provinces') return okReply(provinces);
    if (call.path.startsWith('/api/addresses/wards')) return okReply(wards);
    if (call.path === '/api/checkout/previews') return previewReply(call);
    if (call.path === '/api/orders') return orderReply(call);
    return null;
  });

  return {
    ...api,
    orderCalls: () => api.callsTo('POST', '/api/orders'),
    previewCalls: () => api.callsTo('POST', '/api/checkout/previews'),
  };
}

function checkoutRoutes() {
  return (
    <Routes>
      <Route path="/checkout" element={<CheckoutPage />} />
      <Route path="/orders/:code/success" element={<OrderSuccessPage />} />
      <Route path="/orders/:code/payment" element={<p>Trang thanh toán</p>} />
      <Route path="/cart" element={<p>Trang giỏ hàng</p>} />
    </Routes>
  );
}

async function openCheckout() {
  await renderWithProviders(root, checkoutRoutes(), '/checkout');
}

function setValue(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const prototype =
    element instanceof HTMLTextAreaElement
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, 'value')?.set?.call(element, value);
  element.dispatchEvent(new Event('input', { bubbles: true }));
}

function field(id: string) {
  const element = container.querySelector<HTMLInputElement | HTMLTextAreaElement>(`#${id}`);
  if (!element) throw new Error(`Không thấy ô #${id}`);
  return element;
}

async function typeInto(id: string, value: string) {
  await act(async () => setValue(field(id), value));
}

async function pause(milliseconds: number) {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, milliseconds));
  });
}

// Đợi quá thời gian debounce rồi nhường vòng sự kiện, để preview đã gọi và đã vẽ xong.
async function waitForPreview() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, PREVIEW_DEBOUNCE_MS + 60));
  });
  await settle();
}

async function chooseOption(inputId: string, typed: string, optionName: string) {
  await act(async () => {
    field(inputId).focus();
    setValue(field(inputId), typed);
  });
  const option = [...container.querySelectorAll<HTMLElement>('li[role="option"]')].find(
    (item) => item.textContent === optionName,
  );
  if (!option) throw new Error(`Không thấy lựa chọn ${optionName}`);
  await act(async () => option.click());
  await settle();
}

async function fillAddress(line = '12 Phố Test') {
  await chooseOption('checkout-address-province', 'ha noi', 'Hà Nội');
  await chooseOption('checkout-address-ward', 'ba dinh', 'Phường Ba Đình');
  await typeInto('checkout-address-line', line);
  await waitForPreview();
}

async function fillContact() {
  await typeInto('checkout-contact-name', 'Nguyễn Văn An');
  await typeInto('checkout-contact-phone', '0912345678');
  await typeInto('checkout-contact-email', 'an@example.com');
}

function submitButton() {
  const button = container.querySelector<HTMLButtonElement>('button[type="submit"]');
  if (!button) throw new Error('Không thấy nút Đặt hàng');
  return button;
}

async function clickSubmit() {
  await act(async () => submitButton().click());
  await settle();
}

const summaryText = () =>
  container.querySelector('aside[aria-label="Tóm tắt đơn hàng"]')?.textContent ?? '';
const errorText = (id: string) => container.querySelector(`#${id}-error`)?.textContent ?? null;
const locationText = () => container.querySelector('[data-testid="location"]')?.textContent;

function orderBodyOf(call: RecordedCall | undefined) {
  return call?.body as Record<string, unknown>;
}

function idempotencyKeysOf(api: ReturnType<typeof stubCheckoutApi>): string[] {
  return api.fetchMock.mock.calls
    .filter((call) => String(call[0]) === '/api/orders')
    .map((call) => (call[1]?.headers as Record<string, string>)['Idempotency-Key'] ?? '');
}

describe('CheckoutPage: giỏ không đặt được', () => {
  it('giỏ rỗng không hiện form và có liên kết về giỏ hàng', async () => {
    stubCheckoutApi({}, buildCart([]));
    await openCheckout();

    expect(container.querySelector('form')).toBeNull();
    expect(container.textContent).toContain('Giỏ hàng đang trống.');
    expect(container.querySelector('a[href="/cart"]')).not.toBeNull();
  });

  it('giỏ có dòng không mua được không hiện form', async () => {
    const cart = buildCart([
      buildCartLine({ purchasable: false, unavailableReason: 'out_of_stock' }),
    ]);
    const api = stubCheckoutApi({}, cart);
    await openCheckout();

    expect(container.querySelector('form')).toBeNull();
    expect(container.querySelector('a[href="/cart"]')).not.toBeNull();
    expect(api.previewCalls()).toHaveLength(0);
  });
});

describe('CheckoutPage: preview và tóm tắt tiền', () => {
  it('chưa có địa chỉ thì hiện tạm tính của giỏ, câu gợi ý và tắt nút đặt hàng', async () => {
    const api = stubCheckoutApi();
    await openCheckout();

    expect(summaryText()).toContain('20.000.000 đ');
    expect(summaryText()).toContain('Chọn địa chỉ để tính phí vận chuyển');
    expect(submitButton().disabled).toBe(true);
    expect(api.previewCalls()).toHaveLength(0);
  });

  it('hiện tạm tính, phí vận chuyển, phần miễn phí và tổng tiền của preview', async () => {
    stubCheckoutApi({
      previewReply: () =>
        okReply(
          buildPreview({
            subtotal: 52_000_000,
            shippingFee: 35_000,
            shippingDiscount: 35_000,
            customerShippingFee: 0,
            total: 52_000_000,
          }),
        ),
    });
    await openCheckout();
    await fillAddress();

    const text = summaryText();
    expect(text).toContain('Tạm tính');
    expect(text).toContain('35.000 đ');
    expect(text).toContain('Miễn phí vận chuyển');
    expect(text).toContain('-35.000 đ');
    expect(text).toContain('Tổng tiền');
    expect(text).toContain('52.000.000 đ');
    expect(submitButton().disabled).toBe(false);
  });

  it('preview không có ngày giao dự kiến thì trang không hiện ngày giao hay chữ phí dự phòng', async () => {
    // Phí đã lưu và phí dự phòng của shipping không có ngày giao, nên `expectedDeliveryDate` là `null`.
    stubCheckoutApi({
      previewReply: () => okReply(buildPreview({ expectedDeliveryDate: null })),
    });
    await openCheckout();
    await fillAddress();

    const pageText = container.textContent ?? '';
    expect(summaryText()).toContain('30.000 đ');
    expect(pageText).not.toMatch(/dự kiến/i);
    expect(pageText).not.toMatch(/ngày giao/i);
    expect(pageText).not.toMatch(/giao dự kiến/i);
    expect(pageText).not.toMatch(/dự phòng/i);
    expect(pageText).not.toMatch(/fallback/i);
  });

  it('gõ số nhà liên tục chỉ gọi preview một lần sau khi ngừng gõ', async () => {
    const api = stubCheckoutApi();
    await openCheckout();
    await chooseOption('checkout-address-province', 'ha noi', 'Hà Nội');
    await chooseOption('checkout-address-ward', 'ba dinh', 'Phường Ba Đình');

    // Mỗi lần gõ cách nhau 50 ms, ngắn hơn nhiều so với 400 ms debounce, nên chỉ lần gõ cuối được gửi.
    await typeInto('checkout-address-line', '1');
    await pause(50);
    await typeInto('checkout-address-line', '12');
    await pause(50);
    await typeInto('checkout-address-line', '12 Phố');
    await pause(50);
    expect(api.previewCalls()).toHaveLength(0);

    await waitForPreview();
    expect(api.previewCalls()).toHaveLength(1);
    expect(orderBodyOf(api.previewCalls()[0])).toEqual({
      address: { provinceCode: '201', wardCode: '1A0101', line: '12 Phố' },
      paymentMethod: 'cod',
    });
  });

  it('đổi phương thức thanh toán thì gọi lại preview với phương thức mới', async () => {
    const api = stubCheckoutApi();
    await openCheckout();
    await fillAddress();
    expect(api.previewCalls()).toHaveLength(1);

    await act(async () => field('checkout-payment-sepay').click());
    await settle();

    expect(api.previewCalls()).toHaveLength(2);
    expect(orderBodyOf(api.previewCalls()[1]).paymentMethod).toBe('sepay');
  });
});

describe('CheckoutPage: hạn mức COD', () => {
  it('tắt lựa chọn COD kèm lý do có số tiền hạn mức khi preview không cho COD', async () => {
    stubCheckoutApi({
      previewReply: (call) =>
        (call.body as { paymentMethod: string }).paymentMethod === 'sepay'
          ? okReply(buildPreview({ codAllowed: false, codMaxAmount: 20_000_000 }))
          : errorReply(422, 'COD_NOT_ALLOWED', 'Vượt hạn mức COD.'),
    });
    await openCheckout();
    await fillAddress();

    // Preview đầu với COD bị từ chối nên trang tự chuyển sang chuyển khoản và gọi lại preview.
    expect(field('checkout-payment-sepay')).toHaveProperty('checked', true);
    const cod = field('checkout-payment-cod') as HTMLInputElement;
    expect(cod.disabled).toBe(true);
    expect(container.textContent).toContain('Đơn trên 20.000.000 đ chỉ thanh toán chuyển khoản');
    expect(container.textContent).toContain(
      'Đơn hàng vượt hạn mức thanh toán khi nhận hàng. Trang đã chuyển sang chuyển khoản.',
    );
    expect(submitButton().disabled).toBe(false);
  });

  it('giữ COD dùng được khi preview cho phép COD', async () => {
    stubCheckoutApi();
    await openCheckout();
    await fillAddress();

    const cod = field('checkout-payment-cod') as HTMLInputElement;
    expect(cod.disabled).toBe(false);
    expect(cod.checked).toBe(true);
  });
});

describe('CheckoutPage: lỗi preview', () => {
  it('SHIPPING_UNAVAILABLE hiện đúng câu, tắt nút, không hiện phí và cho thử lại', async () => {
    let attempt = 0;
    const api = stubCheckoutApi({
      previewReply: () => {
        attempt += 1;
        return attempt === 1
          ? errorReply(503, 'SHIPPING_UNAVAILABLE', 'Đơn vị vận chuyển đang gián đoạn.')
          : okReply(buildPreview());
      },
    });
    await openCheckout();
    await fillAddress();

    expect(summaryText()).toContain('Chưa tính được phí vận chuyển, thử lại');
    expect(summaryText()).not.toContain('Phí vận chuyển');
    expect(summaryText()).not.toContain('Tổng tiền');
    expect(submitButton().disabled).toBe(true);

    const retry = [...container.querySelectorAll('button')].find(
      (button) => button.textContent === 'Thử lại',
    );
    expect(retry).toBeDefined();
    await act(async () => retry?.click());
    await settle();

    expect(api.previewCalls()).toHaveLength(2);
    expect(summaryText()).toContain('Tổng tiền');
    expect(submitButton().disabled).toBe(false);
  });

  it('lỗi có key address hiện dưới ô địa chỉ', async () => {
    stubCheckoutApi({
      previewReply: () =>
        errorReply(400, 'VALIDATION_ERROR', 'Dữ liệu không hợp lệ.', [
          { key: 'address', message: 'Địa chỉ nhận hàng không còn trong danh mục.' },
        ]),
    });
    await openCheckout();
    await fillAddress();

    expect(errorText('checkout-address')).toBe('Địa chỉ nhận hàng không còn trong danh mục.');
    expect(submitButton().disabled).toBe(true);
  });

  it('SKU_UNAVAILABLE hiện thông báo kèm liên kết về giỏ hàng', async () => {
    stubCheckoutApi({
      previewReply: () => errorReply(422, 'SKU_UNAVAILABLE', 'Giỏ hàng có sản phẩm không thể mua.'),
    });
    await openCheckout();
    await fillAddress();

    expect(container.querySelector('[role="alert"]')?.textContent).toContain(
      'Giỏ hàng có sản phẩm không thể mua.',
    );
    expect(container.querySelector('a[href="/cart"]')).not.toBeNull();
    expect(submitButton().disabled).toBe(true);
  });

  it('TOO_MANY_REQUESTS hiện thông báo của API', async () => {
    stubCheckoutApi({
      previewReply: () =>
        errorReply(429, 'TOO_MANY_REQUESTS', 'Bạn thao tác quá nhanh, thử lại sau.'),
    });
    await openCheckout();
    await fillAddress();

    expect(summaryText()).toContain('Bạn thao tác quá nhanh, thử lại sau.');
    expect(submitButton().disabled).toBe(true);
  });
});

describe('CheckoutPage: kiểm form bằng Zod', () => {
  it('bấm Đặt hàng khi form sai thì hiện lỗi dưới đúng ô, focus ô đầu tiên, không gửi đơn', async () => {
    const api = stubCheckoutApi();
    await openCheckout();
    await fillAddress();
    await typeInto('checkout-contact-phone', '123');

    await clickSubmit();

    expect(errorText('checkout-contact-name')).toBe('Cần nhập họ và tên.');
    expect(errorText('checkout-contact-phone')).toBe('Số điện thoại không đúng định dạng.');
    expect(errorText('checkout-contact-email')).toBe('Cần nhập email.');
    expect(field('checkout-contact-phone').getAttribute('aria-invalid')).toBe('true');
    expect(field('checkout-contact-phone').getAttribute('aria-describedby')).toBe(
      'checkout-contact-phone-error',
    );
    expect(document.activeElement?.id).toBe('checkout-contact-name');
    expect(api.orderCalls()).toHaveLength(0);
  });

  it('kiểm ô khi rời ô', async () => {
    stubCheckoutApi();
    await openCheckout();

    await act(async () => {
      field('checkout-contact-email').focus();
      setValue(field('checkout-contact-email'), 'sai-dinh-dang');
      field('checkout-contact-email').blur();
    });

    expect(errorText('checkout-contact-email')).toBe('Email không đúng định dạng.');
  });

  it('bật hóa đơn thì hiện 4 ô bắt buộc và kiểm mã số thuế', async () => {
    const api = stubCheckoutApi();
    await openCheckout();
    await fillAddress();
    await fillContact();

    expect(container.querySelector('#checkout-invoice-tax-code')).toBeNull();
    await act(async () => field('checkout-invoice-toggle').click());
    expect(container.querySelector('#checkout-invoice-company-name')).not.toBeNull();
    expect(container.querySelector('#checkout-invoice-tax-code')).not.toBeNull();
    expect(container.querySelector('#checkout-invoice-address')).not.toBeNull();
    expect(container.querySelector('#checkout-invoice-email')).not.toBeNull();

    await typeInto('checkout-invoice-company-name', 'Công ty Pickory');
    await typeInto('checkout-invoice-tax-code', '12345');
    await typeInto('checkout-invoice-address', '1 Đường Công Ty');
    await typeInto('checkout-invoice-email', 'ketoan@example.com');
    await clickSubmit();

    expect(errorText('checkout-invoice-tax-code')).toBe(
      'Mã số thuế gồm 10 hoặc 13 chữ số, có thể có dấu gạch ngang.',
    );
    expect(api.orderCalls()).toHaveLength(0);
  });

  it('chỉ email hóa đơn và địa chỉ công ty lỗi thì focus ô nằm trước trên form', async () => {
    const api = stubCheckoutApi();
    await openCheckout();
    await fillAddress();
    await fillContact();
    await act(async () => field('checkout-invoice-toggle').click());
    await typeInto('checkout-invoice-company-name', 'Công ty Pickory');
    await typeInto('checkout-invoice-tax-code', '0312345678');

    await clickSubmit();

    expect(errorText('checkout-invoice-email')).toBe('Cần nhập email nhận hóa đơn.');
    expect(errorText('checkout-invoice-address')).toBe('Cần nhập địa chỉ hóa đơn.');
    // Trên form, ô email hóa đơn nằm trước ô địa chỉ công ty.
    expect(document.activeElement?.id).toBe('checkout-invoice-email');
    expect(api.orderCalls()).toHaveLength(0);
  });
});

describe('CheckoutPage: đặt đơn', () => {
  it('gửi expectedTotal bằng tổng của preview, rồi sang trang thành công với token trong state', async () => {
    const api = stubCheckoutApi();
    await openCheckout();
    await fillAddress();
    await fillContact();
    await typeInto('checkout-note', '  Giao giờ hành chính  ');

    await clickSubmit();

    expect(api.orderCalls()).toHaveLength(1);
    expect(orderBodyOf(api.orderCalls()[0])).toEqual({
      contact: { name: 'Nguyễn Văn An', phone: '0912345678', email: 'an@example.com' },
      address: { provinceCode: '201', wardCode: '1A0101', line: '12 Phố Test' },
      note: 'Giao giờ hành chính',
      paymentMethod: 'cod',
      invoice: null,
      expectedTotal: 20_030_000,
    });
    expect(idempotencyKeysOf(api)[0]).toMatch(/^[A-Za-z0-9_-]{16,100}$/);

    // Token chỉ đi qua state của router. Đường dẫn và query không được chứa token.
    expect(locationText()).toBe('/orders/DH-0001/success');
    expect(container.textContent).toContain('DH-0001');
    const viewLink = container.querySelector<HTMLAnchorElement>('a[href^="/orders/DH-0001?t="]');
    expect(viewLink?.getAttribute('href')).toBe('/orders/DH-0001?t=token-abc');
  });

  it('chuyển đơn SePay tới trang thanh toán cùng token để tải lại vẫn xem được', async () => {
    const api = stubCheckoutApi({
      orderReply: () =>
        okReply(
          {
            code: 'PKABCDEFGH',
            status: 'pending_payment',
            paymentStatus: 'unpaid',
            paymentMethod: 'sepay',
            total: 20_030_000,
            accessToken: 'guest&token',
          },
          201,
        ),
    });
    await openCheckout();
    await fillAddress();
    await fillContact();
    await act(async () => field('checkout-payment-sepay').click());
    await waitForPreview();

    await clickSubmit();

    expect(api.orderCalls()).toHaveLength(1);
    expect(locationText()).toBe('/orders/PKABCDEFGH/payment?t=guest%26token');
  });

  it('nạp lại giỏ sau khi đặt đơn thành công', async () => {
    const api = stubCheckoutApi();
    await openCheckout();
    await fillAddress();
    await fillContact();
    const cartFetchesBefore = api.callsTo('GET', '/api/cart').length;

    await clickSubmit();

    expect(api.callsTo('GET', '/api/cart').length).toBeGreaterThanOrEqual(cartFetchesBefore);
    // Cache giỏ bị đặt lại, nên trang thành công không còn giữ dữ liệu giỏ cũ.
    expect(locationText()).toBe('/orders/DH-0001/success');
  });

  it('bấm Đặt hàng nhiều lần nhanh, kể cả gửi form bằng mã, chỉ gửi một request', async () => {
    let releaseOrder: (reply: FakeApiReply) => void = () => {};
    const gate = new Promise<FakeApiReply>((resolve) => {
      releaseOrder = resolve;
    });
    const api = stubCheckoutApi({ orderReply: () => gate });
    await openCheckout();
    await fillAddress();
    await fillContact();

    await act(async () => {
      submitButton().click();
      submitButton().click();
      // Gửi form bằng mã, như phím Enter trong ô nhập. Cách này không đi qua nút đã tắt.
      container.querySelector('form')?.requestSubmit();
    });
    await settle();

    expect(api.orderCalls()).toHaveLength(1);
    expect(submitButton().disabled).toBe(true);
    expect(submitButton().textContent).toBe('Đang đặt hàng...');

    await act(async () => releaseOrder(okReply({ code: 'DH-0002', accessToken: null }, 201)));
    await settle();
  });

  it('ORDER_TOTAL_CHANGED hiện tổng mới, yêu cầu xác nhận và không tự đặt lại', async () => {
    let previewCount = 0;
    const api = stubCheckoutApi({
      previewReply: () => {
        previewCount += 1;
        return okReply(buildPreview(previewCount === 1 ? {} : { total: 20_130_000 }));
      },
      orderReply: () => errorReply(409, 'ORDER_TOTAL_CHANGED', 'Giá hoặc giỏ hàng đã thay đổi.'),
    });
    await openCheckout();
    await fillAddress();
    await fillContact();

    await clickSubmit();

    const alert = container.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('20.130.000 đ');
    expect(alert?.textContent).toContain('kiểm tra lại');
    expect(summaryText()).toContain('20.130.000 đ');
    expect(api.orderCalls()).toHaveLength(1);
    expect(locationText()).toBe('/checkout');
  });

  it('tải lại preview lỗi shipping sau ORDER_TOTAL_CHANGED thì không hiện phí cũ và tắt nút', async () => {
    let previewCount = 0;
    stubCheckoutApi({
      previewReply: () => {
        previewCount += 1;
        return previewCount === 1
          ? okReply(buildPreview())
          : errorReply(503, 'SHIPPING_UNAVAILABLE', 'Đơn vị vận chuyển đang gián đoạn.');
      },
      orderReply: () => errorReply(409, 'ORDER_TOTAL_CHANGED', 'Giá hoặc giỏ hàng đã thay đổi.'),
    });
    await openCheckout();
    await fillAddress();
    await fillContact();
    expect(summaryText()).toContain('Tổng tiền');

    await clickSubmit();

    expect(summaryText()).toContain('Chưa tính được phí vận chuyển, thử lại');
    expect(summaryText()).not.toContain('Tổng tiền');
    expect(summaryText()).not.toContain('47.000');
    expect(submitButton().disabled).toBe(true);
  });

  it('dùng cùng một Idempotency-Key cho mọi lần bấm trong một lần mở trang', async () => {
    const api = stubCheckoutApi({
      orderReply: () => errorReply(409, 'ORDER_TOTAL_CHANGED', 'Giá hoặc giỏ hàng đã thay đổi.'),
    });
    await openCheckout();
    await fillAddress();
    await fillContact();

    await clickSubmit();
    await clickSubmit();
    await clickSubmit();

    const keys = idempotencyKeysOf(api);
    expect(keys).toHaveLength(3);
    expect(new Set(keys).size).toBe(1);
  });

  it('INSUFFICIENT_STOCK hiện danh sách SKU thiếu và liên kết về giỏ hàng', async () => {
    stubCheckoutApi({
      orderReply: () =>
        errorReply(409, 'INSUFFICIENT_STOCK', 'Một số sản phẩm không đủ hàng.', [
          { key: 'sku-1', message: 'Sản phẩm này chỉ còn 1, trong khi giỏ hàng có 2.' },
        ]),
    });
    await openCheckout();
    await fillAddress();
    await fillContact();

    await clickSubmit();

    const alert = container.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Một số sản phẩm không đủ hàng.');
    expect(alert?.textContent).toContain(
      'Laptop Kaze: Sản phẩm này chỉ còn 1, trong khi giỏ hàng có 2.',
    );
    expect(container.querySelector('a[href="/cart"]')).not.toBeNull();
    expect(locationText()).toBe('/checkout');
  });

  it('lỗi 400 hiện dưới đúng ô theo key và focus vào ô lỗi đầu tiên', async () => {
    stubCheckoutApi({
      orderReply: () =>
        errorReply(400, 'VALIDATION_ERROR', 'Dữ liệu không hợp lệ.', [
          { key: 'contact.email', message: 'Email không đúng định dạng.' },
          { key: 'contact.phone', message: 'Số điện thoại không đúng định dạng.' },
        ]),
    });
    await openCheckout();
    await fillAddress();
    await fillContact();

    await clickSubmit();

    expect(errorText('checkout-contact-phone')).toBe('Số điện thoại không đúng định dạng.');
    expect(errorText('checkout-contact-email')).toBe('Email không đúng định dạng.');
    expect(document.activeElement?.id).toBe('checkout-contact-phone');
  });

  it('ADDRESS_NOT_DELIVERABLE hiện dưới ô địa chỉ, giữ dữ liệu đã nhập và focus ô đó', async () => {
    const message =
      'Đơn vị vận chuyển không nhận địa chỉ này. Kiểm tra tên đường có thuộc phường/xã đã chọn không.';
    stubCheckoutApi({
      orderReply: () =>
        errorReply(422, 'ADDRESS_NOT_DELIVERABLE', message, [{ key: 'address.line', message }]),
    });
    await openCheckout();
    await fillAddress('99 Đường Sai');
    await fillContact();

    await clickSubmit();

    expect(errorText('checkout-address-line')).toBe(message);
    const line = field('checkout-address-line');
    expect(line.getAttribute('aria-invalid')).toBe('true');
    expect(line.getAttribute('aria-describedby')).toContain('checkout-address-line');
    expect(document.activeElement?.id).toBe('checkout-address-line');
    // Khách không phải nhập lại: mọi ô giữ giá trị đã gõ.
    expect(line.value).toBe('99 Đường Sai');
    expect(field('checkout-contact-name').value).toBe('Nguyễn Văn An');
    expect(field('checkout-contact-phone').value).toBe('0912345678');
    expect(field('checkout-contact-email').value).toBe('an@example.com');
    expect(locationText()).toBe('/checkout');
  });

  it('lỗi khác hiện bằng thông báo lỗi chung', async () => {
    stubCheckoutApi({ orderReply: () => errorReply(500, 'SERVER_ERROR', 'Máy chủ gặp sự cố.') });
    await openCheckout();
    await fillAddress();
    await fillContact();

    await clickSubmit();

    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Máy chủ gặp sự cố.');
  });
});

describe('CheckoutPage: truy cập', () => {
  it('mọi ô nhập có nhãn và nhóm phương thức thanh toán nằm trong fieldset có legend', async () => {
    stubCheckoutApi();
    await openCheckout();
    await act(async () => field('checkout-invoice-toggle').click());

    const controls = [
      ...container.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>(
        'input:not([type="hidden"]), textarea',
      ),
    ];
    expect(controls.length).toBeGreaterThan(8);
    for (const control of controls) {
      expect(control.labels?.length, `Ô ${control.id} thiếu nhãn`).toBeGreaterThan(0);
    }

    const legend = container.querySelector('fieldset legend');
    expect(legend?.textContent).toBe('Phương thức thanh toán');
    expect(legend?.parentElement?.querySelectorAll('input[type="radio"]')).toHaveLength(2);
  });

  it('vùng số tiền có aria-live', async () => {
    stubCheckoutApi();
    await openCheckout();

    const liveRegion = container.querySelector('aside [aria-live="polite"]');
    expect(liveRegion).not.toBeNull();
  });
});
