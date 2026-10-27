// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { CartPage } from '../../../../../../src/features/commerce/customer/cart-page';
import { PaymentPage } from '../../../../../../src/features/commerce/customer/checkout/payment-page';

const orderCode = 'PKABCDEFGH';
const initialTime = Date.parse('2030-01-02T03:00:00Z');
let container: HTMLDivElement;
let root: Root;
let queryClient: QueryClient;
let payment: Record<string, unknown>;
let calls: { method: string; path: string; body: unknown }[];
let clipboard: Mock;

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

async function flush() {
  for (let index = 0; index < 8; index += 1) {
    await act(async () => {
      await Promise.resolve();
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
  }
}

async function renderPayment() {
  await act(async () =>
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/orders/${orderCode}/payment?t=guest-token`]}>
          <Routes>
            <Route path="/orders/:code/payment" element={<PaymentPage />} />
            <Route path="/orders/:code/success" element={<p>Shop đã nhận tiền chuyển khoản</p>} />
            <Route path="/orders/:code" element={<p>Chi tiết đơn</p>} />
            <Route path="/cart" element={<CartPage />} />
          </Routes>
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
  );
  await flush();
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(initialTime);
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
  payment = {
    code: orderCode,
    status: 'pending_payment',
    paymentStatus: 'unpaid',
    paymentMethod: 'sepay',
    total: 123456,
    paymentDeadline: new Date(initialTime + 600000).toISOString(),
    bankAccountNumber: '00123456',
    bankCode: 'VCB',
  };
  calls = [];
  clipboard = vi.fn().mockResolvedValue(undefined);
  vi.stubGlobal(
    'navigator',
    Object.assign(Object.create(window.navigator), { clipboard: { writeText: clipboard } }),
  );
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input);
      const method = init?.method ?? 'GET';
      const body = typeof init?.body === 'string' ? JSON.parse(init.body) : null;
      calls.push({ method, path, body });
      if (path.includes('/payment')) return Response.json({ data: payment });
      if (path.endsWith(`/orders/${orderCode}?t=guest-token`))
        return Response.json({
          data: {
            code: orderCode,
            items: [
              { skuId: 'sku-1', quantity: 2 },
              { skuId: 'sku-2', quantity: 1 },
            ],
          },
        });
      if (path === '/api/cart/items') return Response.json({ data: { items: [] } });
      return Response.json({ error: { code: 'NOT_FOUND', message: 'Not found' } }, { status: 404 });
    }),
  );
});

afterEach(async () => {
  await act(async () => root.unmount());
  queryClient.clear();
  container.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('PaymentPage', () => {
  it('hiện bước thanh toán và tổng tiền của đơn', async () => {
    await renderPayment();

    const currentStep = container.querySelector('.purchase-progress-step[data-state="current"]');
    expect(currentStep?.textContent).toContain('Thanh toán và hoàn tất');
    expect(container.querySelector('.payment-summary-total strong')?.textContent).toBe('123.456 đ');
  });

  it('hỏi sau mỗi 3 giây và dừng hẳn khi rời trang', async () => {
    await renderPayment();
    expect(calls.filter((call) => call.path.includes('/payment'))).toHaveLength(1);
    await act(async () => vi.advanceTimersByTime(3000));
    await flush();
    expect(calls.filter((call) => call.path.includes('/payment'))).toHaveLength(2);
    await act(async () => root.unmount());
    await act(async () => vi.advanceTimersByTime(12000));
    expect(calls.filter((call) => call.path.includes('/payment'))).toHaveLength(2);
  });

  it('chuyển sang trang thành công khi ngân hàng đã xác nhận', async () => {
    await renderPayment();
    payment = { ...payment, paymentStatus: 'paid' };
    await act(async () => vi.advanceTimersByTime(3000));
    await flush();
    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe(
      `/orders/${orderCode}/success`,
    );
  });

  it('hết hạn thì ẩn mã QR nhưng tiếp tục hỏi máy chủ', async () => {
    payment = { ...payment, paymentDeadline: new Date(initialTime + 1000).toISOString() };
    await renderPayment();
    expect(container.querySelector('img[alt*="QR"]')).not.toBeNull();
    await act(async () => vi.advanceTimersByTime(1000));
    await flush();
    expect(container.textContent).toContain('Thời gian thanh toán đã hết');
    expect(container.querySelector('img[alt*="QR"]')).toBeNull();
    expect(container.querySelector('button[aria-label^="Sao chép"]')).toBeNull();
    await act(async () => vi.advanceTimersByTime(2000));
    await flush();
    expect(calls.filter((call) => call.path.includes('/payment'))).toHaveLength(2);
  });

  it('đơn bị hủy trước hạn không bị gắn nhãn quá hạn', async () => {
    payment = { ...payment, status: 'cancelled' };
    await renderPayment();
    expect(container.textContent).toContain('Đơn hàng đã hủy');
    expect(container.textContent).not.toContain('quá hạn');
    expect(container.textContent).not.toContain('Thời gian thanh toán đã hết');
    expect(container.querySelector('img[alt*="QR"]')).toBeNull();
    expect(container.querySelector('button[aria-label^="Sao chép"]')).toBeNull();
    expect(container.querySelector('.payment-state button')?.textContent).toBe('Đặt lại đơn');
  });

  it('sao chép thông tin và báo lỗi clipboard cho khách', async () => {
    await renderPayment();
    const copy = container.querySelector(
      'button[aria-label="Sao chép số tài khoản"]',
    ) as HTMLButtonElement;
    await act(async () => copy.click());
    expect(clipboard).toHaveBeenCalledWith('00123456');
    expect(container.querySelector('[aria-live="polite"]')?.textContent).toContain(
      'Đã sao chép số tài khoản',
    );
    const contentCopy = container.querySelector(
      'button[aria-label="Sao chép nội dung chuyển khoản"]',
    ) as HTMLButtonElement;
    await act(async () => contentCopy.click());
    expect(clipboard).toHaveBeenCalledWith(orderCode);
    expect(container.querySelector('[aria-live="polite"]')?.textContent).toContain(
      'Đã sao chép nội dung chuyển khoản',
    );
    clipboard.mockRejectedValueOnce(new Error('blocked'));
    await act(async () => copy.click());
    expect(container.querySelector('[aria-live="polite"]')?.textContent).toContain(
      'sao chép thủ công',
    );
  });

  it('thêm lại từng dòng vào giỏ và thông báo số dòng không thêm được', async () => {
    payment = { ...payment, status: 'cancelled' };
    await renderPayment();
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input);
      calls.push({
        method: init?.method ?? 'GET',
        path,
        body: typeof init?.body === 'string' ? JSON.parse(init.body) : null,
      });
      if (path.includes('/payment')) return Response.json({ data: payment });
      if (path.includes(`/orders/${orderCode}?`))
        return Response.json({
          data: {
            items: [
              { skuId: 'sku-1', quantity: 2 },
              { skuId: 'sku-2', quantity: 1 },
            ],
          },
        });
      if (path === '/api/cart/items' && JSON.parse(String(init?.body)).skuId === 'sku-2')
        return Response.json(
          { error: { code: 'OUT_OF_STOCK', message: 'Hết hàng' } },
          { status: 409 },
        );
      return Response.json({ data: { items: [] } });
    });
    const button = Array.from(container.querySelectorAll('button')).find((element) =>
      element.textContent?.includes('Đặt lại đơn'),
    )!;
    await act(async () => button.click());
    await flush();
    expect(calls.filter((call) => call.path === '/api/cart/items')).toHaveLength(2);
    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe('/cart');
    expect(container.textContent).toContain('1 dòng hàng không thêm được vào giỏ');
  });
  it('đơn COD chuyển về chi tiết đơn mà không hiện QR', async () => {
    payment = { ...payment, paymentMethod: 'cod' };
    await renderPayment();
    expect(container.querySelector('[data-testid="location"]')?.textContent).toBe(
      `/orders/${orderCode}?t=guest-token`,
    );
  });

  it('thiếu cấu hình nhận tiền không hiển thị mã QR', async () => {
    payment = { ...payment, bankCode: null };
    await renderPayment();
    expect(container.textContent).toContain('Shop chưa sẵn sàng nhận chuyển khoản');
    expect(container.querySelector('img[alt*="QR"]')).toBeNull();
  });

  it('mã đơn không tồn tại hiển thị đường về trang chủ', async () => {
    vi.mocked(fetch).mockResolvedValue(
      Response.json(
        { error: { code: 'ORDER_NOT_FOUND', message: 'Không tìm thấy đơn.' } },
        { status: 404 },
      ),
    );
    await renderPayment();
    expect(container.textContent).toContain('Không tìm thấy đơn hàng');
    expect(container.querySelector('a[href="/"]')?.textContent).toBe('Về trang chủ');
  });

  it('mất mạng khi hỏi lại vẫn giữ thông tin thanh toán và báo đang kết nối lại', async () => {
    await renderPayment();
    vi.mocked(fetch).mockRejectedValueOnce(new Error('offline'));
    await act(async () => vi.advanceTimersByTime(3000));
    await flush();
    expect(container.querySelector('img[alt*="QR"]')).not.toBeNull();
    expect(container.textContent).toContain('Đang kết nối lại');
  });
});
