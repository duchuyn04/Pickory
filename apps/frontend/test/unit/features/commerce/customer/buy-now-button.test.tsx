// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BuyNowButton } from '../../../../../src/features/commerce/customer/buy-now-button';
import { skuBlack256 } from '../../catalog/customer/product-fixtures';
import { buildCart } from './cart-fixtures';
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

async function renderButton() {
  await renderWithProviders(
    root,
    <Routes>
      <Route path="/" element={<BuyNowButton sku={skuBlack256} isDiscontinued={false} />} />
      <Route path="/checkout" element={<p>Trang đặt hàng</p>} />
    </Routes>,
    '/',
  );
}

describe('BuyNowButton', () => {
  it('thêm đúng SKU rồi chuyển tới checkout', async () => {
    const api = stubCartApi(buildCart([]));
    await renderButton();

    await act(async () => {
      [...container.querySelectorAll('button')]
        .find((button) => button.textContent === 'Mua ngay')
        ?.click();
    });
    await settle();

    expect(api.callsTo('POST', '/api/cart/items')[0]?.body).toEqual({
      skuId: 'sku-black-256',
      quantity: 1,
    });
    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe('/checkout');
  });

  it('thêm đúng số lượng được chọn rồi chuyển tới checkout', async () => {
    const api = stubCartApi(buildCart([]));
    await renderWithProviders(
      root,
      <Routes>
        <Route
          path="/"
          element={<BuyNowButton sku={skuBlack256} isDiscontinued={false} quantity={3} />}
        />
        <Route path="/checkout" element={<p>Trang đặt hàng</p>} />
      </Routes>,
      '/',
    );

    await act(async () => {
      [...container.querySelectorAll('button')]
        .find((button) => button.textContent === 'Mua ngay')
        ?.click();
    });
    await settle();

    // Gửi đúng số lượng đã chọn lên API thay vì mặc định 1.
    expect(api.callsTo('POST', '/api/cart/items')[0]?.body).toEqual({
      skuId: 'sku-black-256',
      quantity: 3,
    });
    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe('/checkout');
  });

  it('lỗi API thì ở lại trang và hiện thông báo', async () => {
    stubCartApi(buildCart([]), (call) =>
      call.method === 'POST'
        ? {
            status: 422,
            body: { error: { code: 'OUT_OF_STOCK', message: 'Sản phẩm đã hết hàng.' } },
          }
        : null,
    );
    await renderButton();

    await act(async () => {
      [...container.querySelectorAll('button')]
        .find((button) => button.textContent === 'Mua ngay')
        ?.click();
    });
    await settle();

    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe('/');
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(
      'Sản phẩm đã hết hàng.',
    );
  });
});
