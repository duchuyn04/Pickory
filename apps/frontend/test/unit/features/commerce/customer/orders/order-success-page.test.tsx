// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OrderSuccessPage } from '../../../../../../src/features/commerce/customer/checkout/order-success-page';

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

function renderOrderSuccess(
  code: string,
  state: { accessToken?: string | null; paymentMethod?: string | null } | null,
  actor: { kind: string; userId?: string } | null,
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  if (actor) {
    queryClient.setQueryData(['actor'], actor);
  }

  return act(async () => {
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter
          initialEntries={[
            {
              pathname: `/orders/${code}/success`,
              state,
            },
          ]}
        >
          <Routes>
            <Route path="/orders/:code/success" element={<OrderSuccessPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  });
}

describe('OrderSuccessPage', () => {
  it('hiển thị mã đơn và liên kết xem đơn kèm token khi có accessToken trong state', async () => {
    // Khách có token thì nhận liên kết xem đơn kèm token truy cập.
    await renderOrderSuccess(
      'PK12345678',
      { accessToken: 'token-abc', paymentMethod: 'cod' },
      null,
    );

    expect(container.textContent).toContain('PK12345678');
    const link = container.querySelector('a.btn-primary') as HTMLAnchorElement | null;
    expect(link).not.toBeNull();
    expect(link?.getAttribute('href')).toBe('/orders/PK12345678?t=token-abc');
    expect(link?.textContent).toBe('Xem đơn hàng');
  });

  it('hiển thị liên kết xem đơn không kèm token khi khách đã đăng nhập và không có token trong state', async () => {
    // Khách đăng nhập mở lại trang nhận liên kết về chi tiết đơn không kèm token.
    await renderOrderSuccess('PK12345678', null, { kind: 'customer', userId: 'user-1' });

    expect(container.textContent).toContain('PK12345678');
    const link = container.querySelector('a.btn-primary') as HTMLAnchorElement | null;
    expect(link).not.toBeNull();
    expect(link?.getAttribute('href')).toBe('/orders/PK12345678');
  });

  it('không hiển thị liên kết và thông báo gửi email khi khách vãng lai không có token', async () => {
    // Khách vãng lai không có token thì không hiện nút xem đơn mà hướng dẫn mở email.
    await renderOrderSuccess('PK12345678', null, { kind: 'guest' });

    expect(container.textContent).toContain('PK12345678');
    const link = container.querySelector('a.btn-primary');
    expect(link).toBeNull();
    expect(container.textContent).toContain('Liên kết xem đơn đã gửi tới email của bạn.');
  });

  it('hiển thị thông báo chờ thanh toán khi phương thức là sepay', async () => {
    // Đơn chuyển khoản ngân hàng SePay có dòng lưu ý chờ thanh toán.
    await renderOrderSuccess(
      'PK12345678',
      { accessToken: 'token-abc', paymentMethod: 'sepay' },
      null,
    );

    expect(container.textContent).toContain('Đơn hàng đang chờ thanh toán chuyển khoản ngân hàng.');
  });

  it('không hiện thông báo email hoặc liên kết khi tài khoản còn tải và không có token', async () => {
    // Giữ yêu cầu tài khoản chờ để kiểm tra trạng thái trước khi xác định khách.
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>(() => {})),
    );
    await renderOrderSuccess('PK12345678', null, null);
    expect(container.querySelector('a.btn-primary')).toBeNull();
    expect(container.textContent).not.toContain('Liên kết xem đơn đã gửi tới email của bạn.');
  });
});
