// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MiniCart } from '../../../../../src/features/commerce/customer/mini-cart';
import type { ActorData } from '../../../../../src/features/identity/hooks/use-actor';
import { buildCart, buildCartLine } from './cart-fixtures';
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

const toggle = () => container.querySelector<HTMLButtonElement>('button.mini-cart-toggle');
const panel = () => container.querySelector<HTMLElement>('[role="dialog"]');

async function openPanel() {
  await act(async () => toggle()?.click());
  await settle();
}

describe('MiniCart', () => {
  it('hiện số dòng trên badge và mô tả bằng aria-label', async () => {
    stubCartApi(buildCart([buildCartLine(), buildCartLine({ skuId: 'sku-2' })]));
    await renderWithProviders(root, <MiniCart />);

    expect(toggle()?.getAttribute('aria-label')).toBe('Giỏ hàng, 2 sản phẩm');
    expect(container.querySelector('.mini-cart-badge')?.textContent).toBe('2');
  });

  it('giỏ trống thì không hiện badge', async () => {
    stubCartApi(buildCart([]));
    await renderWithProviders(root, <MiniCart />);

    expect(toggle()?.getAttribute('aria-label')).toBe('Giỏ hàng, trống');
    expect(container.querySelector('.mini-cart-badge')).toBeNull();
  });

  it('mở panel liệt kê dòng, tạm tính và liên kết tới trang giỏ', async () => {
    stubCartApi(buildCart([buildCartLine({ quantity: 2, lineTotal: 40_000_000 })]));
    await renderWithProviders(root, <MiniCart />);

    await openPanel();

    expect(toggle()?.getAttribute('aria-expanded')).toBe('true');
    expect(panel()?.textContent).toContain('Laptop Kaze');
    expect(panel()?.textContent).toContain('40.000.000 đ');
    expect(panel()?.querySelector('.mini-cart-subtotal')?.textContent).toContain('40.000.000 đ');
    expect(panel()?.querySelector('a[href="/cart"]')?.textContent).toBe('Xem giỏ hàng');
  });

  it('chuyển focus vào panel khi mở, đóng bằng Esc và trả focus về nút', async () => {
    stubCartApi(buildCart([buildCartLine()]));
    await renderWithProviders(root, <MiniCart />);
    toggle()?.focus();

    await openPanel();
    expect(panel()?.contains(document.activeElement)).toBe(true);

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    });
    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(toggle());
    expect(toggle()?.getAttribute('aria-expanded')).toBe('false');
  });

  it('đóng khi bấm ra ngoài panel', async () => {
    stubCartApi(buildCart([buildCartLine()]));
    await renderWithProviders(root, <MiniCart />);
    await openPanel();

    await act(async () => {
      document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    });

    expect(panel()).toBeNull();
  });

  it('bấm vào bên trong panel không đóng panel', async () => {
    stubCartApi(buildCart([buildCartLine()]));
    await renderWithProviders(root, <MiniCart />);
    await openPanel();

    await act(async () => {
      panel()?.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    });

    expect(panel()).not.toBeNull();
  });

  it('dòng không mua được hiện nhãn lý do và không cộng vào tạm tính', async () => {
    stubCartApi(
      buildCart([
        buildCartLine({ skuId: 'sku-ok', lineTotal: 1_000_000, unitPrice: 1_000_000 }),
        buildCartLine({
          skuId: 'sku-off',
          purchasable: false,
          unavailableReason: 'out_of_stock',
        }),
      ]),
    );
    await renderWithProviders(root, <MiniCart />);

    await openPanel();

    expect(panel()?.textContent).toContain('Hết hàng');
    expect(panel()?.querySelector('.mini-cart-subtotal')?.textContent).toContain('1.000.000 đ');
  });

  it('giỏ trống thì panel báo trống', async () => {
    stubCartApi(buildCart([]));
    await renderWithProviders(root, <MiniCart />);

    await openPanel();

    expect(panel()?.textContent).toContain('Giỏ hàng đang trống.');
  });

  it('nạp lại giỏ khi người dùng đăng nhập, vì backend đã gộp giỏ vãng lai vào tài khoản', async () => {
    const api = stubCartApi(buildCart([buildCartLine()]));
    const queryClient = await renderWithProviders(root, <MiniCart />);
    const cartGets = () =>
      api.callsTo('GET', '/api/cart').filter((call) => call.path === '/api/cart');
    expect(cartGets()).toHaveLength(1);

    const customer: ActorData = {
      anonymousId: 'anon-1',
      userId: 'user-1',
      kind: 'customer',
      roles: ['customer'],
      permissions: [],
      consent: { personalization: false, marketing: false },
    };
    api.setCart(buildCart([buildCartLine(), buildCartLine({ skuId: 'sku-merged' })]));
    await act(async () => queryClient.setQueryData(['actor'], customer));
    await settle();

    expect(cartGets()).toHaveLength(2);
    expect(container.querySelector('.mini-cart-badge')?.textContent).toBe('2');
  });
});
