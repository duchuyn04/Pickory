// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CustomerOrdersPage } from '../../../../../../src/features/commerce/customer/orders/customer-orders-page';
import type { CustomerOrdersPagedResponse } from '../../../../../../src/features/commerce/customer/orders/orders-api';
import { settle } from '../cart-test-utils';

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

function stubOrdersListApi(pagedResponse: CustomerOrdersPagedResponse) {
  const calls: { url: string; method: string }[] = [];
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const method = init?.method ?? 'GET';
    calls.push({ url, method });
    return new Response(JSON.stringify({ data: pagedResponse }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    });
  });

  vi.stubGlobal('fetch', fetchMock);
  return { calls };
}

async function renderCustomerOrders(
  actor: { kind: string; userId?: string } | null,
  initialUrl = '/me/orders',
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  if (actor) {
    queryClient.setQueryData(['actor'], actor);
  }

  await act(async () => {
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[initialUrl]}>
          <Routes>
            <Route path="/me/orders" element={<CustomerOrdersPage />} />
            <Route path="/login" element={<div>Trang đăng nhập</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  });

  await settle();
  return queryClient;
}

describe('CustomerOrdersPage', () => {
  it('chuyển hướng tới trang đăng nhập khi người dùng chưa đăng nhập', async () => {
    // Khách vãng lai truy cập /me/orders bị chuyển hướng về /login.
    stubOrdersListApi({ items: [], total: 0, page: 1, limit: 20 });
    await renderCustomerOrders({ kind: 'guest' });

    expect(container.textContent).toContain('Trang đăng nhập');
  });

  it('hiển thị thông báo khi tài khoản chưa có đơn hàng nào', async () => {
    // Tài khoản mới chưa có đơn hàng thì hiện thông báo rỗng.
    stubOrdersListApi({ items: [], total: 0, page: 1, limit: 20 });
    await renderCustomerOrders({ kind: 'customer', userId: 'user-1' });

    expect(container.textContent).toContain('Bạn chưa có đơn hàng nào.');
    const shopLink = container.querySelector('a.btn-outline') as HTMLAnchorElement | null;
    expect(shopLink?.getAttribute('href')).toBe('/');
  });

  it('hiển thị danh sách các đơn hàng với liên kết, ảnh sản phẩm, trạng thái và tổng tiền', async () => {
    // Dựng danh sách 2 đơn hàng: một đơn có ảnh, một đơn không có ảnh.
    stubOrdersListApi({
      items: [
        {
          code: 'PK11111111',
          status: 'pending_confirmation',
          paymentStatus: 'unpaid',
          paymentMethod: 'cod',
          total: 1_200_000,
          createdAt: '2026-10-12T10:00:00Z',
          itemCount: 2,
          firstItemName: 'Bàn phím cơ Pickory Key 1',
          firstItemImagePublicId: 'img-1',
          firstItemImageUrl: 'https://res.cloudinary.com/test/image/upload/img-1',
        },
        {
          code: 'PK22222222',
          status: 'delivered',
          paymentStatus: 'paid',
          paymentMethod: 'sepay',
          total: 500_000,
          createdAt: '2026-10-10T08:00:00Z',
          itemCount: 1,
          firstItemName: 'Chuột Pickory Mouse',
          firstItemImagePublicId: null,
          firstItemImageUrl: null,
        },
      ],
      total: 2,
      page: 1,
      limit: 20,
    });

    await renderCustomerOrders({ kind: 'customer', userId: 'user-1' });

    // Kiểm tra thông tin đơn 1.
    expect(container.textContent).toContain('PK11111111');
    expect(container.textContent).toContain('Chờ xác nhận');
    expect(container.textContent).toContain('1.200.000 đ');
    expect(container.textContent).toContain('Bàn phím cơ Pickory Key 1 (2 sản phẩm)');

    // Liên kết tới chi tiết không kèm token.
    const link1 = container.querySelector('a.customer-order-code') as HTMLAnchorElement | null;
    expect(link1?.getAttribute('href')).toBe('/orders/PK11111111');

    // Kiểm tra ảnh: đơn 1 có ảnh, đơn 2 có placeholder.
    const imgEl = container.querySelector('img.order-line-image');
    expect(imgEl).not.toBeNull();
    const placeholder = container.querySelector('.product-image-placeholder');
    expect(placeholder).not.toBeNull();
  });

  it('hiển thị phân trang và cho phép chuyển trang khi có nhiều trang đơn hàng', async () => {
    // Dựng 25 đơn hàng với giới hạn 20 đơn mỗi trang (tổng cộng 2 trang).
    const api = stubOrdersListApi({
      items: [
        {
          code: 'PK11111111',
          status: 'pending_confirmation',
          paymentStatus: 'unpaid',
          paymentMethod: 'cod',
          total: 1_000_000,
          createdAt: '2026-10-12T10:00:00Z',
          itemCount: 1,
          firstItemName: 'Sản phẩm 1',
          firstItemImagePublicId: null,
          firstItemImageUrl: null,
        },
      ],
      total: 25,
      page: 1,
      limit: 20,
    });

    await renderCustomerOrders({ kind: 'customer', userId: 'user-1' });

    expect(container.textContent).toContain('Trang 1 / 2 (25 đơn hàng)');
    const nextBtn = Array.from(container.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Trang sau'),
    );
    expect(nextBtn).toBeDefined();

    // Bấm Trang sau để chuyển trang.
    await act(async () => nextBtn?.click());
    await settle();

    const lastCall = api.calls[api.calls.length - 1];
    expect(lastCall?.url).toContain('page=2');
  });

  it('hiện liên kết trang đầu khi trang vượt giới hạn nhưng tài khoản vẫn có đơn', async () => {
    // Trang ngoài giới hạn không được báo tài khoản chưa có đơn.
    stubOrdersListApi({ items: [], total: 2, page: 99, limit: 20 });
    await renderCustomerOrders({ kind: 'customer', userId: 'user-1' }, '/me/orders?page=99');
    expect(container.textContent).not.toContain('Bạn chưa có đơn hàng nào.');
    expect(container.querySelector('a[href="/me/orders?page=1"]')?.textContent).toBe('Về trang 1');
  });
});
