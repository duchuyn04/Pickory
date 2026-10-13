// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CheckoutPage } from '../../../../../../src/features/commerce/customer/checkout/checkout-page';
import { PREVIEW_DEBOUNCE_MS } from '../../../../../../src/features/commerce/customer/checkout/use-checkout-preview';
import { buildCart, buildCartLine } from '../cart-fixtures';
import { renderWithProviders, settle, stubCartApi, type FakeApiReply } from '../cart-test-utils';

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

const provinces = [
  { code: '201', name: 'Hà Nội' },
  { code: '202', name: 'Hồ Chí Minh' },
];
const wards = [{ code: '1A0101', name: 'Phường Ba Đình' }];

function savedAddress(id: string, overrides: Record<string, unknown> = {}) {
  return {
    id,
    label: `Nhãn ${id}`,
    name: `Người nhận ${id}`,
    phone: '0987000111',
    isDefault: false,
    address: {
      provinceCode: '201',
      provinceName: 'Hà Nội',
      wardCode: '1A0101',
      wardName: 'Phường Ba Đình',
      line: `${id} Phố Test`,
    },
    ...overrides,
  };
}

const customerActor = {
  anonymousId: 'anon-1',
  userId: 'user-1',
  kind: 'customer',
  email: 'khach@example.com',
  name: 'Tên Trong Hồ Sơ',
  phone: '0900000000',
  roles: ['customer'],
  permissions: [],
  consent: { personalization: false, marketing: false },
};

function ok(data: unknown): FakeApiReply {
  return { status: 200, body: { data } };
}

const preview = {
  subtotal: 20_000_000,
  shippingFee: 30_000,
  shippingDiscount: 0,
  customerShippingFee: 30_000,
  total: 20_030_000,
  freeShippingThreshold: 50_000_000,
  codMaxAmount: 30_000_000,
  codAllowed: true,
  expectedDeliveryDate: null,
};

interface Options {
  actor?: Record<string, unknown> | null;
  addresses?: unknown[];
  // Trả một lời hứa chưa hoàn tất để giữ phản hồi sổ địa chỉ lại.
  addressesReply?: () => Promise<FakeApiReply>;
}

function stubApi({ actor = customerActor, addresses = [], addressesReply }: Options = {}) {
  return stubCartApi(buildCart([buildCartLine()]), (call) => {
    if (call.path === '/api/me' && actor) return ok(actor);
    if (call.path === '/api/me/addresses') return addressesReply ? addressesReply() : ok(addresses);
    if (call.path === '/api/addresses/provinces') return ok(provinces);
    if (call.path.startsWith('/api/addresses/wards')) return ok(wards);
    if (call.path === '/api/checkout/previews') return ok(preview);
    return null;
  });
}

async function openCheckout() {
  await renderWithProviders(
    root,
    <Routes>
      <Route path="/checkout" element={<CheckoutPage />} />
    </Routes>,
    '/checkout',
  );
}

function field(id: string) {
  return container.querySelector<HTMLInputElement>(`#${id}`)!;
}

function setValue(element: HTMLInputElement, value: string) {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(element, value);
  element.dispatchEvent(new Event('input', { bubbles: true }));
}

async function waitForPreview() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, PREVIEW_DEBOUNCE_MS + 60));
  });
  await settle();
}

function savedSelect() {
  return container.querySelector<HTMLSelectElement>('#checkout-saved-address');
}

async function choose(select: HTMLSelectElement, value: string) {
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!.call(select, value);
    select.dispatchEvent(new Event('change', { bubbles: true }));
  });
  await settle();
}

describe('CheckoutPage: sổ địa chỉ đã lưu', () => {
  it('điền sẵn địa chỉ mặc định, tên và số điện thoại của địa chỉ đó', async () => {
    stubApi({
      addresses: [savedAddress('mac-dinh', { isDefault: true }), savedAddress('khac')],
    });
    await openCheckout();

    expect(field('checkout-contact-name').value).toBe('Người nhận mac-dinh');
    expect(field('checkout-contact-phone').value).toBe('0987000111');
    // Email không có trong sổ địa chỉ nên vẫn lấy từ hồ sơ.
    expect(field('checkout-contact-email').value).toBe('khach@example.com');
    expect(field('checkout-address-province').value).toBe('Hà Nội');
    expect(field('checkout-address-ward').value).toBe('Phường Ba Đình');
    expect(field('checkout-address-line').value).toBe('mac-dinh Phố Test');
  });

  it('điền sẵn xong thì gọi preview phí ship với địa chỉ đó', async () => {
    const api = stubApi({ addresses: [savedAddress('mac-dinh', { isDefault: true })] });
    await openCheckout();
    await waitForPreview();

    const previews = api.callsTo('POST', '/api/checkout/previews');
    expect(previews).toHaveLength(1);
    expect(JSON.stringify(previews[0]!.body)).toContain('"provinceCode":"201"');
    expect(JSON.stringify(previews[0]!.body)).toContain('"wardCode":"1A0101"');
    expect(JSON.stringify(previews[0]!.body)).toContain('mac-dinh Phố Test');
  });

  it('có ô chọn Địa chỉ đã lưu liệt kê các địa chỉ', async () => {
    stubApi({ addresses: [savedAddress('a', { isDefault: true }), savedAddress('b')] });
    await openCheckout();

    const select = savedSelect()!;
    const labels = [...select.options].map((option) => option.textContent);
    expect(labels.some((text) => text?.includes('Nhãn a'))).toBe(true);
    expect(labels.some((text) => text?.includes('Nhãn b'))).toBe(true);
  });

  it('chọn địa chỉ khác điền lại người nhận và địa chỉ', async () => {
    stubApi({
      addresses: [
        savedAddress('a', { isDefault: true }),
        savedAddress('b', {
          name: 'Trần Thị Bình',
          phone: '0911222333',
          address: {
            provinceCode: '202',
            provinceName: 'Hồ Chí Minh',
            wardCode: '1A0101',
            wardName: 'Phường Ba Đình',
            line: '45 Lê Lợi',
          },
        }),
      ],
    });
    await openCheckout();

    await choose(savedSelect()!, 'b');

    expect(field('checkout-contact-name').value).toBe('Trần Thị Bình');
    expect(field('checkout-contact-phone').value).toBe('0911222333');
    expect(field('checkout-address-province').value).toBe('Hồ Chí Minh');
    expect(field('checkout-address-line').value).toBe('45 Lê Lợi');
  });

  it('khách sửa tay số nhà sau khi chọn vẫn được', async () => {
    stubApi({ addresses: [savedAddress('a', { isDefault: true })] });
    await openCheckout();

    await act(async () => setValue(field('checkout-address-line'), '77 Phố Mới'));

    expect(field('checkout-address-line').value).toBe('77 Phố Mới');
  });

  it('khách đã tự gõ tên trước khi danh sách tải xong thì không bị ghi đè', async () => {
    let release: (reply: FakeApiReply) => void = () => undefined;
    const held = new Promise<FakeApiReply>((resolve) => {
      release = resolve;
    });
    stubApi({ addressesReply: () => held });
    await openCheckout();

    await act(async () => setValue(field('checkout-contact-name'), 'Tên Tự Gõ'));
    await act(async () => release(ok([savedAddress('a', { isDefault: true })])));
    await settle();

    expect(field('checkout-contact-name').value).toBe('Tên Tự Gõ');
    // Ô còn trống thì vẫn được điền sẵn.
    expect(field('checkout-contact-phone').value).toBe('0987000111');
    expect(field('checkout-address-line').value).toBe('a Phố Test');
  });

  it('khách vãng lai không gọi API sổ địa chỉ', async () => {
    const api = stubApi({ actor: null });
    await openCheckout();

    expect(api.callsTo('GET', '/api/me/addresses')).toHaveLength(0);
    expect(savedSelect()).toBeNull();
  });

  it('nhân viên không gọi API sổ địa chỉ', async () => {
    const api = stubApi({ actor: { ...customerActor, kind: 'staff' } });
    await openCheckout();

    expect(api.callsTo('GET', '/api/me/addresses')).toHaveLength(0);
  });

  it('khách chưa có địa chỉ không thấy ô chọn và vẫn được điền sẵn từ hồ sơ', async () => {
    stubApi({ addresses: [] });
    await openCheckout();

    expect(savedSelect()).toBeNull();
    expect(field('checkout-contact-name').value).toBe('Tên Trong Hồ Sơ');
    expect(field('checkout-contact-phone').value).toBe('0900000000');
  });

  it('không có địa chỉ mặc định thì không tự điền địa chỉ nhưng vẫn có ô chọn', async () => {
    stubApi({ addresses: [savedAddress('a'), savedAddress('b')] });
    await openCheckout();

    expect(savedSelect()).not.toBeNull();
    expect(field('checkout-address-province').value).toBe('');
  });
});
