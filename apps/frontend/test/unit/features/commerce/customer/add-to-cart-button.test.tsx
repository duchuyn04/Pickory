// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AddToCartButton } from '../../../../../src/features/commerce/customer/add-to-cart-button';
import { MiniCart } from '../../../../../src/features/commerce/customer/mini-cart';
import { skuBlack256, skuBlack512 } from '../../catalog/customer/product-fixtures';
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

const button = () =>
  [...container.querySelectorAll('button')].find(
    (candidate) => candidate.textContent === 'Thêm vào giỏ',
  ) as HTMLButtonElement;

async function click() {
  await act(async () => button().click());
  await settle();
}

describe('AddToCartButton', () => {
  it('gửi POST với SKU đang chọn, số lượng 1 và không có source', async () => {
    const api = stubCartApi(buildCart([]));
    await renderWithProviders(root, <AddToCartButton sku={skuBlack256} isDiscontinued={false} />);

    await click();

    const posts = api.callsTo('POST', '/api/cart/items');
    expect(posts).toHaveLength(1);
    expect(posts[0]?.body).toEqual({ skuId: 'sku-black-256', quantity: 1 });
  });

  it('thành công thì hiện xác nhận có vùng aria-live', async () => {
    stubCartApi(buildCart([]));
    await renderWithProviders(root, <AddToCartButton sku={skuBlack256} isDiscontinued={false} />);

    await click();

    const status = container.querySelector('[role="status"]');
    expect(status?.textContent).toBe('Đã thêm vào giỏ');
    expect(status?.getAttribute('aria-live')).toBe('polite');
  });

  it('badge mini cart tăng sau khi thêm', async () => {
    stubCartApi(buildCart([]));
    await renderWithProviders(
      root,
      <>
        <MiniCart />
        <AddToCartButton sku={skuBlack256} isDiscontinued={false} />
      </>,
    );
    expect(container.querySelector('.mini-cart-badge')).toBeNull();

    await click();

    expect(container.querySelector('.mini-cart-badge')?.textContent).toBe('1');
  });

  it('tắt nút trong lúc đang gửi để không bấm hai lần', async () => {
    let release: (() => void) | undefined;
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input) === '/api/cart' && (init?.method ?? 'GET') === 'GET') {
        return Response.json({ data: buildCart([]) });
      }
      await new Promise<void>((resolve) => {
        release = resolve;
      });
      return Response.json({ data: buildCart([]) });
    });
    vi.stubGlobal('fetch', fetchMock);
    await renderWithProviders(root, <AddToCartButton sku={skuBlack256} isDiscontinued={false} />);

    await act(async () => button().click());
    // TanStack Query báo trạng thái đang gửi sau một vòng sự kiện, nên chờ trước khi kiểm.
    await settle();
    expect(button().disabled).toBe(true);
    await act(async () => button().click());
    const posts = fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST');
    expect(posts).toHaveLength(1);

    await act(async () => release?.());
    await settle();
    expect(button().disabled).toBe(false);
  });

  it.each([
    ['SKU_UNAVAILABLE', 'Sản phẩm này hiện không thể mua.'],
    ['SKU_NOT_FOUND', 'Không tìm thấy sản phẩm này.'],
    ['CART_LIMIT_EXCEEDED', 'Mỗi sản phẩm chỉ được thêm tối đa 5 vào giỏ hàng.'],
  ])('lỗi %s hiện thông báo của API cạnh nút', async (code, message) => {
    stubCartApi(buildCart([]), (call) =>
      call.method === 'POST' ? { status: 422, body: { error: { code, message } } } : null,
    );
    await renderWithProviders(root, <AddToCartButton sku={skuBlack256} isDiscontinued={false} />);

    await click();

    expect(container.querySelector('[role="alert"]')?.textContent).toContain(message);
    expect(container.querySelector('[role="status"]')?.textContent).toBe('');
  });

  it('tắt khi SKU đang chọn hết hàng', async () => {
    stubCartApi(buildCart([]));
    await renderWithProviders(root, <AddToCartButton sku={skuBlack512} isDiscontinued={false} />);

    expect(button().disabled).toBe(true);
  });

  it('tắt khi sản phẩm ngừng kinh doanh', async () => {
    stubCartApi(buildCart([]));
    await renderWithProviders(root, <AddToCartButton sku={skuBlack256} isDiscontinued />);

    expect(button().disabled).toBe(true);
  });

  it('tắt khi chưa có SKU để chọn', async () => {
    stubCartApi(buildCart([]));
    await renderWithProviders(root, <AddToCartButton sku={null} isDiscontinued={false} />);

    expect(button().disabled).toBe(true);
  });
});
