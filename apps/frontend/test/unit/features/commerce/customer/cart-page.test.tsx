// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CartPage } from '../../../../../src/features/commerce/customer/cart-page';
import { MiniCart } from '../../../../../src/features/commerce/customer/mini-cart';
import { buildCart, buildCartLine, buildRemovedLine } from './cart-fixtures';
import { renderWithProviders, settle, stubCartApi } from './cart-test-utils';

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

function cartRoutes() {
  return (
    <Routes>
      <Route path="/cart" element={<CartPage />} />
      <Route path="/checkout" element={<p>Trang đặt hàng</p>} />
      <Route path="/" element={<p>Trang chủ</p>} />
    </Routes>
  );
}

const lineElement = (skuId = 'sku-1') =>
  container.querySelector<HTMLElement>(`li.cart-line[data-sku-id="${skuId}"]`);
const quantityInput = (skuId = 'sku-1') =>
  lineElement(skuId)?.querySelector<HTMLInputElement>('input[type="number"]') ?? null;
const checkoutLink = () => container.querySelector<HTMLAnchorElement>('a.cart-checkout');
const locationText = () => container.querySelector('[data-testid="location"]')?.textContent;

function buttonIn(line: HTMLElement | null, name: string) {
  return [...(line?.querySelectorAll('button') ?? [])].find((button) =>
    (button.getAttribute('aria-label') ?? '').startsWith(name),
  );
}

// Đổi giá trị ô số lượng như người dùng gõ. React cần setter gốc để nhận sự kiện input.
async function typeQuantity(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  await act(async () => {
    setter?.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function commit(input: HTMLInputElement, how: 'blur' | 'enter') {
  await act(async () => {
    if (how === 'enter') {
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    } else {
      input.blur();
      input.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
    }
  });
  await settle();
}

describe('CartPage: hiển thị', () => {
  it('hiện tên liên kết tới sản phẩm, tùy chọn, đơn giá, thành tiền và tạm tính', async () => {
    stubCartApi(buildCart([buildCartLine({ quantity: 2, lineTotal: 40_000_000 })]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    const line = lineElement();
    expect(container.querySelector('h1')?.textContent).toBe('Giỏ hàng');
    expect(line?.querySelector('.cart-line-name a')?.getAttribute('href')).toBe('/p/laptop-kaze');
    expect(line?.textContent).toContain('Màu: Đen');
    expect(line?.textContent).toContain('20.000.000 đ');
    expect(line?.querySelector('.cart-line-total')?.textContent).toBe('40.000.000 đ');
    expect(container.querySelector('.cart-summary')?.textContent).toContain('40.000.000 đ');
  });

  it('hiện bước giỏ hàng, tiêu đề tóm tắt và liên kết tiếp tục mua sắm', async () => {
    stubCartApi(buildCart([buildCartLine()]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    const currentStep = container.querySelector('.purchase-progress-step[data-state="current"]');
    const summary = container.querySelector('.cart-summary');
    expect(currentStep?.textContent).toContain('Giỏ hàng');
    expect(summary?.querySelector('h2')?.textContent).toBe('Tóm tắt đơn hàng');
    expect(summary?.querySelector('a[href="/"]')?.textContent).toBe('Tiếp tục mua sắm');
  });

  it('ô số lượng có label gắn đúng ô', async () => {
    stubCartApi(buildCart([buildCartLine({ quantity: 3 })]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    const input = quantityInput();
    const label = container.querySelector<HTMLLabelElement>(`label[for="${input?.id}"]`);
    expect(label?.textContent).toBe('Số lượng của Laptop Kaze');
    expect(input?.value).toBe('3');
  });

  it('giỏ trống hiện trạng thái trống có liên kết về trang chủ', async () => {
    stubCartApi(buildCart([]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    expect(container.textContent).toContain('Giỏ hàng của bạn đang trống');
    expect(container.querySelector('a[href="/"]')?.textContent).toBe('Tiếp tục mua sắm');
    expect(checkoutLink()).toBeNull();
  });

  it('hiện lỗi của API khi không đọc được giỏ', async () => {
    stubCartApi(buildCart([]), (call) =>
      call.path === '/api/cart'
        ? { status: 500, body: { error: { code: 'INTERNAL_ERROR', message: 'Máy chủ gặp lỗi.' } } }
        : null,
    );
    await renderWithProviders(root, cartRoutes(), '/cart');

    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Máy chủ gặp lỗi.');
  });
});

describe('CartPage: dòng không mua được', () => {
  it('hiện nhãn lý do và tắt nút đặt hàng kèm lời giải thích', async () => {
    stubCartApi(
      buildCart([
        buildCartLine({ skuId: 'sku-ok' }),
        buildCartLine({
          skuId: 'sku-off',
          purchasable: false,
          unavailableReason: 'sku_disabled',
        }),
      ]),
    );
    await renderWithProviders(root, cartRoutes(), '/cart', undefined);

    expect(lineElement('sku-off')?.querySelector('.cart-line-reason')?.textContent).toBe(
      'Phiên bản này đã ngừng bán',
    );
    expect(checkoutLink()?.getAttribute('aria-disabled')).toBe('true');
    expect(container.querySelector('.cart-checkout-hint')?.textContent).toBe(
      'Hãy xóa hoặc đổi các sản phẩm không mua được trước khi đặt hàng.',
    );

    await act(async () => checkoutLink()?.click());
    expect(locationText()).toBe('/cart');
  });

  it('nút đặt hàng dẫn tới /checkout khi mọi dòng mua được', async () => {
    stubCartApi(buildCart([buildCartLine()]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    expect(checkoutLink()?.getAttribute('aria-disabled')).toBeNull();
    expect(checkoutLink()?.getAttribute('href')).toBe('/checkout');
    await act(async () => checkoutLink()?.click());
    expect(locationText()).toBe('/checkout');
  });

  it('dòng đã gỡ khỏi catalog hiện "Sản phẩm không còn bán" và chỉ cho xóa', async () => {
    stubCartApi(buildCart([buildRemovedLine('sku-gone', 2)]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    const line = lineElement('sku-gone');
    expect(line?.querySelector('.cart-line-reason')?.textContent).toBe('Sản phẩm không còn bán');
    expect(quantityInput('sku-gone')).toBeNull();
    expect(buttonIn(line, 'Xóa')).toBeDefined();
  });

  it.each(['out_of_stock', 'sku_disabled', 'product_unavailable'] as const)(
    'dòng %s tắt nút tăng nhưng vẫn cho giảm và nhập số lượng',
    async (reason) => {
      stubCartApi(
        buildCart([buildCartLine({ quantity: 3, purchasable: false, unavailableReason: reason })]),
      );
      await renderWithProviders(root, cartRoutes(), '/cart');

      const increase = buttonIn(lineElement(), 'Tăng số lượng') as HTMLButtonElement;
      const decrease = buttonIn(lineElement(), 'Giảm số lượng') as HTMLButtonElement;
      expect(increase.disabled).toBe(true);
      expect(decrease.disabled).toBe(false);
      expect(quantityInput()?.disabled).toBe(false);
    },
  );

  it('dòng chỉ còn một phần tồn kho vẫn cho bấm nút tăng', async () => {
    stubCartApi(
      buildCart([
        buildCartLine({
          quantity: 4,
          stock: 2,
          purchasable: false,
          unavailableReason: 'insufficient_stock',
        }),
      ]),
    );
    await renderWithProviders(root, cartRoutes(), '/cart');

    const increase = buttonIn(lineElement(), 'Tăng số lượng') as HTMLButtonElement;
    expect(increase.disabled).toBe(false);
  });

  it('dòng chỉ còn một phần tồn kho ghi số còn lại', async () => {
    stubCartApi(
      buildCart([
        buildCartLine({
          quantity: 4,
          stock: 2,
          purchasable: false,
          unavailableReason: 'insufficient_stock',
        }),
      ]),
    );
    await renderWithProviders(root, cartRoutes(), '/cart');

    expect(lineElement()?.querySelector('.cart-line-reason')?.textContent).toBe(
      'Chỉ còn 2 sản phẩm',
    );
  });
});

describe('CartPage: đổi số lượng và xóa', () => {
  it('nút tăng gửi PATCH với số lượng mới và cập nhật thành tiền, tạm tính', async () => {
    const api = stubCartApi(buildCart([buildCartLine({ quantity: 1 })]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    await act(async () => buttonIn(lineElement(), 'Tăng số lượng')?.click());
    await settle();

    expect(api.callsTo('PATCH', '/api/cart/items/sku-1')).toEqual([
      { method: 'PATCH', path: '/api/cart/items/sku-1', body: { quantity: 2 } },
    ]);
    expect(quantityInput()?.value).toBe('2');
    expect(lineElement()?.querySelector('.cart-line-total')?.textContent).toBe('40.000.000 đ');
    expect(container.querySelector('.cart-summary')?.textContent).toContain('40.000.000 đ');
  });

  it('nút giảm tại số lượng 1 bị tắt và không gửi request', async () => {
    const api = stubCartApi(buildCart([buildCartLine({ quantity: 1 })]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    const decrease = buttonIn(lineElement(), 'Giảm số lượng') as HTMLButtonElement;
    expect(decrease.disabled).toBe(true);
    expect(api.callsTo('PATCH', '/api/cart/items')).toHaveLength(0);
  });

  it('nhập số hợp lệ rồi nhấn Enter gửi PATCH', async () => {
    const api = stubCartApi(buildCart([buildCartLine({ quantity: 1 })]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    const input = quantityInput() as HTMLInputElement;
    await typeQuantity(input, '3');
    await commit(input, 'enter');

    expect(api.callsTo('PATCH', '/api/cart/items/sku-1').map((call) => call.body)).toEqual([
      { quantity: 3 },
    ]);
    expect(quantityInput()?.value).toBe('3');
  });

  it('giá trị nhỏ hơn 1 không gửi request và ô quay về số lượng trong giỏ', async () => {
    const api = stubCartApi(buildCart([buildCartLine({ quantity: 2 })]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    const input = quantityInput() as HTMLInputElement;
    await typeQuantity(input, '0');
    await commit(input, 'blur');

    expect(api.callsTo('PATCH', '/api/cart/items')).toHaveLength(0);
    expect(quantityInput()?.value).toBe('2');
  });

  it('giá trị không đổi thì không gửi request', async () => {
    const api = stubCartApi(buildCart([buildCartLine({ quantity: 2 })]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    await commit(quantityInput() as HTMLInputElement, 'blur');

    expect(api.callsTo('PATCH', '/api/cart/items')).toHaveLength(0);
  });

  it('nút xóa gửi DELETE và dòng biến mất', async () => {
    const api = stubCartApi(
      buildCart([buildCartLine({ skuId: 'sku-1' }), buildCartLine({ skuId: 'sku-2' })]),
    );
    await renderWithProviders(root, cartRoutes(), '/cart');

    await act(async () => buttonIn(lineElement('sku-1'), 'Xóa')?.click());
    await settle();

    expect(api.callsTo('DELETE', '/api/cart/items/sku-1')).toHaveLength(1);
    expect(lineElement('sku-1')).toBeNull();
    expect(lineElement('sku-2')).not.toBeNull();
  });

  it('xóa dòng cuối thì hiện trạng thái trống', async () => {
    stubCartApi(buildCart([buildCartLine()]));
    await renderWithProviders(root, cartRoutes(), '/cart');

    await act(async () => buttonIn(lineElement(), 'Xóa')?.click());
    await settle();

    expect(container.textContent).toContain('Giỏ hàng của bạn đang trống');
  });
});

describe('CartPage: lỗi CART_LIMIT_EXCEEDED', () => {
  const limitMessage = 'Mỗi sản phẩm chỉ được thêm tối đa 5 vào giỏ hàng.';

  it('hiện thông báo của API cạnh dòng và đưa ô số lượng về giá trị trong giỏ', async () => {
    stubCartApi(buildCart([buildCartLine({ quantity: 5 })]), (call) =>
      call.method === 'PATCH'
        ? {
            status: 422,
            body: { error: { code: 'CART_LIMIT_EXCEEDED', message: limitMessage } },
          }
        : null,
    );
    await renderWithProviders(root, cartRoutes(), '/cart');

    await act(async () => buttonIn(lineElement(), 'Tăng số lượng')?.click());
    await settle();

    const alert = lineElement()?.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain(limitMessage);
    expect(quantityInput()?.value).toBe('5');
  });

  it('gõ số vượt giới hạn rồi Enter: ô nhập quay về số lượng trong giỏ', async () => {
    stubCartApi(buildCart([buildCartLine({ quantity: 2 })]), (call) =>
      call.method === 'PATCH'
        ? {
            status: 422,
            body: { error: { code: 'CART_LIMIT_EXCEEDED', message: limitMessage } },
          }
        : null,
    );
    await renderWithProviders(root, cartRoutes(), '/cart');

    const input = quantityInput() as HTMLInputElement;
    await typeQuantity(input, '9');
    expect(input.value).toBe('9');
    await commit(input, 'enter');

    expect(quantityInput()?.value).toBe('2');
    expect(lineElement()?.querySelector('[role="alert"]')?.textContent).toContain(limitMessage);
  });

  it('thông báo biến mất khi đổi số lượng thành công sau đó', async () => {
    let failNext = true;
    stubCartApi(buildCart([buildCartLine({ quantity: 2 })]), (call) => {
      if (call.method !== 'PATCH' || !failNext) return null;
      failNext = false;
      return {
        status: 422,
        body: { error: { code: 'CART_LIMIT_EXCEEDED', message: limitMessage } },
      };
    });
    await renderWithProviders(root, cartRoutes(), '/cart');

    await act(async () => buttonIn(lineElement(), 'Tăng số lượng')?.click());
    await settle();
    expect(lineElement()?.querySelector('[role="alert"]')).not.toBeNull();

    await act(async () => buttonIn(lineElement(), 'Giảm số lượng')?.click());
    await settle();

    expect(lineElement()?.querySelector('[role="alert"]')).toBeNull();
  });
});

describe('CartPage cùng mini cart: một cache', () => {
  it('đổi trên trang giỏ cập nhật mini cart mà không gọi lại GET /api/cart', async () => {
    const api = stubCartApi(
      buildCart([buildCartLine({ skuId: 'sku-1' }), buildCartLine({ skuId: 'sku-2' })]),
    );
    await renderWithProviders(
      root,
      <>
        <MiniCart />
        {cartRoutes()}
      </>,
      '/cart',
    );
    expect(container.querySelector('.mini-cart-badge')?.textContent).toBe('2');
    const getsBefore = api.callsTo('GET', '/api/cart').filter((call) => call.path === '/api/cart');

    await act(async () => buttonIn(lineElement('sku-1'), 'Xóa')?.click());
    await settle();

    expect(container.querySelector('.mini-cart-badge')?.textContent).toBe('1');
    const getsAfter = api.callsTo('GET', '/api/cart').filter((call) => call.path === '/api/cart');
    expect(getsAfter).toHaveLength(getsBefore.length);
    expect(getsBefore).toHaveLength(1);
  });
});
