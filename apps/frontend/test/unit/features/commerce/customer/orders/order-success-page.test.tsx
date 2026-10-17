// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OrderSuccessPage } from '../../../../../../src/features/commerce/customer/checkout/order-success-page';
import type { OrderDetail } from '../../../../../../src/features/commerce/customer/orders/orders-api';
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

function buildOrderDetail(overrides: Partial<OrderDetail> = {}): OrderDetail {
  return {
    code: 'PK12345678',
    status: 'pending_confirmation',
    paymentStatus: 'unpaid',
    paymentMethod: 'cod',
    createdAt: '2026-10-12T10:00:00Z',
    paymentDeadline: null,
    items: [
      {
        skuId: 'sku-1',
        name: 'Bàn phím cơ Pickory Key',
        variantName: 'Switch đỏ',
        imagePublicId: null,
        imageUrl: null,
        quantity: 2,
        unitPrice: 1_500_000,
        lineTotal: 3_000_000,
      },
    ],
    subtotal: 3_000_000,
    shippingFee: 30_000,
    shippingDiscount: 30_000,
    total: 3_000_000,
    contact: { name: 'Nguyễn Văn Khách', phone: '0912345678', email: 'khach@example.com' },
    shippingAddress: {
      provinceCode: '01',
      provinceName: 'Hà Nội',
      wardCode: '0001',
      wardName: 'Phường Ba Đình',
      line: '15 Đường Đơn Hàng',
    },
    invoice: null,
    statusHistory: [],
    canCancel: true,
    trackingCode: null,
    ...overrides,
  };
}

interface SuccessState {
  accessToken?: string | null;
  paymentMethod?: string | null;
  paymentConfirmed?: boolean;
}

// API giả của chi tiết đơn: chỉ ghi lại URL của `/api/orders`. `reply` có thể trả `Promise` để giữ request đang chạy.
function stubOrderApi(reply: () => Response | Promise<Response>) {
  const urls: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      // Trang tự hỏi tài khoản hiện tại khi test chưa nạp sẵn actor. Trả khách vãng lai cho yêu cầu đó.
      if (url === '/api/me') {
        return Response.json({
          data: { anonymousId: 'anon-1', userId: null, kind: 'guest', roles: [], permissions: [] },
        });
      }
      urls.push(url);
      return reply();
    }),
  );
  return urls;
}

function orderResponse(order: OrderDetail) {
  return Response.json({ data: order });
}

async function renderOrderSuccess(
  code: string,
  state: SuccessState | null,
  actor: { kind: string; userId?: string } | null,
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  if (actor) {
    queryClient.setQueryData(['actor'], actor);
  }

  await act(async () => {
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[{ pathname: `/orders/${code}/success`, state }]}>
          <Routes>
            <Route path="/orders/:code/success" element={<OrderSuccessPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  });
  await settle();
}

describe('OrderSuccessPage', () => {
  it('hiển thị mã đơn và liên kết xem đơn kèm token khi có accessToken trong state', async () => {
    // Khách có token thì nhận liên kết xem đơn kèm token truy cập.
    stubOrderApi(() => orderResponse(buildOrderDetail()));
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
    expect(container.querySelectorAll('a.btn-primary')).toHaveLength(1);
  });

  it('hiển thị liên kết xem đơn không kèm token khi khách đã đăng nhập và không có token trong state', async () => {
    // Khách đăng nhập mở lại trang nhận liên kết về chi tiết đơn không kèm token.
    stubOrderApi(() => orderResponse(buildOrderDetail()));
    await renderOrderSuccess('PK12345678', null, { kind: 'customer', userId: 'user-1' });

    expect(container.textContent).toContain('PK12345678');
    const link = container.querySelector('a.btn-primary') as HTMLAnchorElement | null;
    expect(link).not.toBeNull();
    expect(link?.getAttribute('href')).toBe('/orders/PK12345678');
  });

  it('không hiển thị liên kết xem đơn, không gọi API và dẫn tới trang tra cứu khi khách vãng lai không có token', async () => {
    // Khách vãng lai không có token không có quyền xem đơn nên API chắc chắn trả 404.
    const urls = stubOrderApi(() => orderResponse(buildOrderDetail()));
    await renderOrderSuccess('PK12345678', null, { kind: 'guest' });

    expect(container.textContent).toContain('PK12345678');
    expect(container.querySelector('a.btn-primary')).toBeNull();
    expect(container.textContent).toContain('Liên kết xem đơn đã gửi tới email của bạn.');
    expect(container.querySelector('a[href="/order-lookup"]')).not.toBeNull();
    expect(urls).toHaveLength(0);
  });

  it('gọi chi tiết đơn kèm token trong state', async () => {
    const urls = stubOrderApi(() => orderResponse(buildOrderDetail()));
    await renderOrderSuccess('PK12345678', { accessToken: 'token-abc' }, null);

    expect(urls).toEqual(['/api/orders/PK12345678?t=token-abc']);
  });

  it('gọi chi tiết đơn không kèm token khi khách đã đăng nhập', async () => {
    const urls = stubOrderApi(() => orderResponse(buildOrderDetail()));
    await renderOrderSuccess('PK12345678', null, { kind: 'customer', userId: 'user-1' });

    expect(urls).toEqual(['/api/orders/PK12345678']);
  });

  it('hiện dòng hàng, tổng tiền, người nhận và địa chỉ giao của đơn', async () => {
    stubOrderApi(() => orderResponse(buildOrderDetail()));
    await renderOrderSuccess('PK12345678', { accessToken: 'token-abc' }, null);

    const itemRow = container.querySelector('.order-item-row');
    expect(itemRow?.textContent).toContain('Bàn phím cơ Pickory Key');
    expect(itemRow?.textContent).toContain('Switch đỏ');
    expect(itemRow?.textContent).toContain('Số lượng: 2');
    expect(itemRow?.textContent).toContain('1.500.000 đ');
    expect(itemRow?.textContent).toContain('3.000.000 đ');
    expect(container.querySelector('.order-summary-total')?.textContent).toContain('3.000.000 đ');
    expect(container.textContent).toContain('Miễn phí vận chuyển');
    expect(container.textContent).toContain('Thanh toán khi nhận hàng (COD)');
    expect(container.textContent).toContain('Nguyễn Văn Khách');
    expect(container.textContent).toContain('15 Đường Đơn Hàng, Phường Ba Đình, Hà Nội');
  });

  it('báo đã gửi email xác nhận kèm link xem đơn tới email liên hệ của đơn', async () => {
    stubOrderApi(() => orderResponse(buildOrderDetail()));
    await renderOrderSuccess('PK12345678', { accessToken: 'token-abc' }, null);

    expect(container.querySelector('.order-success-email')?.textContent).toBe(
      'Pickory đã gửi email xác nhận kèm link xem đơn tới khach@example.com.',
    );
  });

  it('hiện các bước tiếp theo theo trạng thái thật của đơn', async () => {
    stubOrderApi(() => orderResponse(buildOrderDetail()));
    await renderOrderSuccess('PK12345678', { accessToken: 'token-abc' }, null);

    const steps = Array.from(container.querySelectorAll('.order-next-step'));
    expect(steps.map((step) => step.getAttribute('data-state'))).toEqual([
      'current',
      'upcoming',
      'upcoming',
    ]);
    expect(steps[0]?.textContent).toContain('Shop xác nhận đơn');
  });

  it('ghi bước thanh toán đã xong khi đơn chuyển khoản đã nhận tiền', async () => {
    stubOrderApi(() =>
      orderResponse(buildOrderDetail({ paymentMethod: 'sepay', paymentStatus: 'paid' })),
    );
    await renderOrderSuccess('PK12345678', { accessToken: 'token-abc' }, null);

    const paymentStep = container.querySelector('.order-next-step[data-state="done"]');
    expect(paymentStep?.textContent).toContain('Shop đã nhận tiền chuyển khoản.');
  });

  it('hiển thị xác nhận đã nhận tiền thay cho trạng thái chờ chuyển khoản', async () => {
    stubOrderApi(() => orderResponse(buildOrderDetail({ paymentMethod: 'sepay' })));
    await renderOrderSuccess(
      'PK12345678',
      { accessToken: 'token-abc', paymentMethod: 'sepay', paymentConfirmed: true },
      null,
    );

    expect(container.textContent).toContain('Shop đã nhận tiền chuyển khoản');
    expect(container.textContent).not.toContain('Đơn hàng đang chờ thanh toán');
  });

  it('hiện khung giữ chỗ khi chi tiết đơn đang tải mà mã đơn và nút vẫn hiện', async () => {
    stubOrderApi(() => new Promise<Response>(() => {}));
    await renderOrderSuccess('PK12345678', { accessToken: 'token-abc' }, null);

    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(container.querySelector('.order-success-code')?.textContent).toBe('PK12345678');
    expect(container.querySelector('a.btn-primary')?.textContent).toBe('Xem đơn hàng');
  });

  it('hiện lỗi tải, vẫn hiện mã đơn và các nút khi chi tiết đơn tải lỗi', async () => {
    stubOrderApi(() =>
      Response.json(
        { error: { code: 'INTERNAL_ERROR', message: 'Không tải được đơn hàng.' } },
        { status: 500 },
      ),
    );
    await renderOrderSuccess('PK12345678', { accessToken: 'token-abc' }, null);

    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Không tải được đơn hàng.');
    expect(container.querySelector('.order-success-code')?.textContent).toBe('PK12345678');
    expect(container.querySelector('a.btn-primary')?.textContent).toBe('Xem đơn hàng');
    expect(container.textContent).toContain('Tiếp tục mua sắm');
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

  it('chép đúng mã đơn vào clipboard và báo Đã sao chép', async () => {
    stubOrderApi(() => orderResponse(buildOrderDetail()));
    const writeText = vi.fn(async () => {});
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } });
    await renderOrderSuccess('PK12345678', { accessToken: 'token-abc' }, null);

    const copyButton = container.querySelector(
      'button[aria-label="Sao chép mã đơn hàng"]',
    ) as HTMLButtonElement;
    expect(container.querySelector('[role="status"]')?.textContent).toBe('');
    await act(async () => copyButton.click());
    await settle();

    expect(writeText).toHaveBeenCalledWith('PK12345678');
    expect(container.querySelector('[role="status"]')?.textContent).toBe('Đã sao chép');
  });
});
