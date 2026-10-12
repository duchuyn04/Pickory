// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OrderDetailPage } from '../../../../../../src/features/commerce/customer/orders/order-detail-page';
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

function buildDetail(overrides: Partial<OrderDetail> = {}): OrderDetail {
  return {
    code: 'PK12345678',
    status: 'pending_confirmation',
    paymentStatus: 'unpaid',
    paymentMethod: 'cod',
    createdAt: '2026-10-12T10:00:00Z',
    paymentDeadline: null,
    items: [
      {
        name: 'Bàn phím cơ Pickory Key 1',
        variantName: 'Xám, Red Switch',
        imagePublicId: 'img-pub-1',
        imageUrl: 'https://res.cloudinary.com/test-cloud/image/upload/img-pub-1',
        quantity: 2,
        unitPrice: 1_000_000,
        lineTotal: 2_000_000,
      },
      {
        name: 'Chuột gaming Pickory Mouse',
        variantName: null,
        imagePublicId: null,
        imageUrl: null,
        quantity: 1,
        unitPrice: 500_000,
        lineTotal: 500_000,
      },
    ],
    subtotal: 2_500_000,
    shippingFee: 30_000,
    shippingDiscount: 10_000,
    total: 2_520_000,
    contact: {
      name: 'Nguyễn Văn An',
      phone: '0901234567',
      email: 'an@example.com',
    },
    shippingAddress: {
      provinceCode: '201',
      provinceName: 'Hà Nội',
      wardCode: '1A0101',
      wardName: 'Phường Ba Đình',
      line: '12 Phố Huế',
    },
    invoice: {
      companyName: 'Công ty TNHH Thử Nghiệm',
      taxCode: '0101234567',
      address: 'Số 1 Đường Thử Nghiệm',
      email: 'invoice@example.com',
    },
    statusHistory: [
      {
        fromStatus: 'pending_confirmation',
        toStatus: 'pending_confirmation',
        createdAt: '2026-10-12T10:00:00Z',
      },
    ],
    canCancel: true,
    trackingCode: null,
    ...overrides,
  };
}

function stubOrderApi(
  initialDetail: OrderDetail | null,
  override?: (url: string, init?: RequestInit) => { status: number; body: unknown } | null,
) {
  let detail = initialDetail;
  const calls: { url: string; method: string }[] = [];

  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    const method = init?.method ?? 'GET';
    calls.push({ url, method });

    if (override) {
      const custom = override(url, init);
      if (custom) {
        return new Response(JSON.stringify(custom.body), {
          status: custom.status,
          headers: { 'Content-Type': 'application/json' },
        });
      }
    }

    if (method === 'GET' && url.includes('/api/orders/')) {
      if (!detail) {
        return new Response(
          JSON.stringify({
            error: { code: 'ORDER_NOT_FOUND', message: 'Không tìm thấy đơn hàng.' },
          }),
          { status: 404, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(JSON.stringify({ data: detail }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }

    if (method === 'POST' && url.includes('/cancellation')) {
      if (!detail) {
        return new Response(
          JSON.stringify({
            error: { code: 'ORDER_NOT_FOUND', message: 'Không tìm thấy đơn hàng.' },
          }),
          { status: 404, headers: { 'Content-Type': 'application/json' } },
        );
      }
      detail = { ...detail, status: 'cancelled', canCancel: false };
      return new Response(JSON.stringify({ data: detail }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }

    return new Response(JSON.stringify({ data: null }), { status: 200 });
  });

  vi.stubGlobal('fetch', fetchMock);
  return { calls, getDetail: () => detail };
}

async function renderOrderDetail(code: string, queryToken?: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const url = queryToken ? `/orders/${code}?t=${queryToken}` : `/orders/${code}`;

  await act(async () => {
    root.render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[url]}>
          <Routes>
            <Route path="/orders/:code" element={<OrderDetailPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  });

  await settle();
  return queryClient;
}

describe('OrderDetailPage', () => {
  it('hiển thị đầy đủ thông tin chi tiết đơn hàng, người nhận, địa chỉ, hóa đơn, tiến trình và ảnh sản phẩm', async () => {
    // Dựng đơn hàng mẫu với đủ các trường dữ liệu.
    stubOrderApi(buildDetail());
    await renderOrderDetail('PK12345678', 'token-123');

    expect(container.textContent).toContain('Đơn hàng #PK12345678');
    expect(container.textContent).toContain('Chờ xác nhận');
    expect(container.textContent).toContain('Chưa thanh toán');
    expect(container.textContent).toContain('Thanh toán khi nhận hàng (COD)');

    // Kiểm tra danh sách sản phẩm.
    expect(container.textContent).toContain('Bàn phím cơ Pickory Key 1');
    expect(container.textContent).toContain('Xám, Red Switch');
    expect(container.textContent).toContain('2.000.000 đ');

    // Kiểm tra ảnh: sản phẩm có ảnh hiện thẻ img, sản phẩm không có ảnh hiện placeholder.
    const images = container.querySelectorAll('.order-line-image');
    expect(images.length).toBe(2);
    const imgEl = container.querySelector('img.order-line-image') as HTMLImageElement | null;
    expect(imgEl).not.toBeNull();
    expect(imgEl?.src).toContain('cloudinary');
    const placeholder = container.querySelector('.product-image-placeholder');
    expect(placeholder).not.toBeNull();

    // Kiểm tra thông tin người nhận và địa chỉ.
    expect(container.textContent).toContain('Nguyễn Văn An');
    expect(container.textContent).toContain('0901234567');
    expect(container.textContent).toContain('12 Phố Huế, Phường Ba Đình, Hà Nội');

    // Kiểm tra thông tin hóa đơn VAT.
    expect(container.textContent).toContain('Công ty TNHH Thử Nghiệm');
    expect(container.textContent).toContain('0101234567');

    // Kiểm tra tóm tắt thanh toán.
    expect(container.textContent).toContain('2.500.000 đ');
    expect(container.textContent).toContain('30.000 đ');
    expect(container.textContent).toContain('-10.000 đ');
    expect(container.textContent).toContain('2.520.000 đ');

    // Kiểm tra tiến trình có thẻ ol.
    const historyList = container.querySelector('ol.order-history-list');
    expect(historyList).not.toBeNull();
  });

  it('hiển thị mã vận đơn khi trackingCode khác null', async () => {
    stubOrderApi(buildDetail({ trackingCode: 'GHN-TRACK-999' }));
    await renderOrderDetail('PK12345678');
    expect(container.textContent).toContain('GHN-TRACK-999');
  });

  it('ẩn mã vận đơn khi trackingCode bằng null', async () => {
    stubOrderApi(buildDetail({ trackingCode: null }));
    await renderOrderDetail('PK12345678');
    expect(container.textContent).not.toContain('Mã vận đơn');
  });

  it('hiển thị thông báo không tìm thấy đơn và liên kết về trang chủ khi API trả 404', async () => {
    // Đơn không tồn tại hoặc không đủ quyền xem.
    stubOrderApi(null);
    await renderOrderDetail('PK99999999');

    expect(container.textContent).toContain('Không tìm thấy đơn hàng.');
    const homeLink = container.querySelector('a.btn-primary') as HTMLAnchorElement | null;
    expect(homeLink?.getAttribute('href')).toBe('/');
  });

  it('nút hủy hiển thị khi canCancel là true', async () => {
    stubOrderApi(buildDetail({ canCancel: true }));
    await renderOrderDetail('PK12345678');
    expect(container.querySelector('button.btn-outline')?.textContent).toContain('Hủy đơn hàng');
  });

  it('nút hủy ẩn khi canCancel là false', async () => {
    stubOrderApi(buildDetail({ canCancel: false }));
    await renderOrderDetail('PK12345678');
    const buttons = Array.from(container.querySelectorAll('button'));
    const cancelBtn = buttons.find((b) => b.textContent?.includes('Hủy đơn hàng'));
    expect(cancelBtn).toBeUndefined();
  });

  it('hộp thoại hủy mở ra khi bấm nút, bấm Giữ đơn không gọi API, bấm Hủy đơn gửi đúng kèm token', async () => {
    const api = stubOrderApi(buildDetail({ canCancel: true }));
    await renderOrderDetail('PK12345678', 'token-abc');

    // Mở hộp thoại xác nhận.
    const openBtn = Array.from(container.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Hủy đơn hàng'),
    )!;
    await act(async () => openBtn.click());

    expect(container.querySelector('[role="dialog"]')).not.toBeNull();

    // Bấm nút Giữ đơn để đóng hộp thoại mà không gọi API.
    const keepBtn = Array.from(container.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Giữ đơn'),
    )!;
    await act(async () => keepBtn.click());

    expect(container.querySelector('[role="dialog"]')).toBeNull();
    expect(api.calls.filter((c) => c.method === 'POST')).toHaveLength(0);

    // Mở lại hộp thoại và bấm Hủy đơn.
    await act(async () => openBtn.click());
    const confirmBtn = container.querySelector(
      '.dialog-foot button.btn-danger',
    ) as HTMLButtonElement;
    await act(async () => confirmBtn.click());
    await settle();

    // Hộp thoại đã đóng và API hủy được gọi đúng URL kèm token.
    expect(container.querySelector('[role="dialog"]')).toBeNull();
    const cancelCall = api.calls.find((c) => c.method === 'POST');
    expect(cancelCall?.url).toContain('/api/orders/PK12345678/cancellation?t=token-abc');
    expect(container.textContent).toContain('Đã hủy đơn hàng.');
    // Nút mở đã biến mất nên thông báo phải nhận focus thay vì phần thân trang.
    expect(document.activeElement).toBe(container.querySelector('[role="status"]'));
  });

  it('hiển thị lỗi từ máy chủ trong hộp thoại khi nhận mã lỗi 409', async () => {
    let detailReadCount = 0;
    const api = stubOrderApi(buildDetail({ canCancel: true }), (url, init) => {
      // Lần đọc sau xung đột phải phản ánh trạng thái mới từ máy chủ.
      if (!init?.method || init.method === 'GET') {
        detailReadCount += 1;
        if (detailReadCount > 1) {
          return {
            status: 200,
            body: { data: buildDetail({ canCancel: false, status: 'confirmed' }) },
          };
        }
      }
      if (init?.method === 'POST') {
        return {
          status: 409,
          body: {
            error: {
              code: 'INVALID_ORDER_TRANSITION',
              message: 'Không thể hủy đơn ở trạng thái này.',
            },
          },
        };
      }
      return null;
    });

    await renderOrderDetail('PK12345678');
    const openBtn = Array.from(container.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Hủy đơn hàng'),
    )!;
    await act(async () => openBtn.click());

    const confirmBtn = container.querySelector(
      '.dialog-foot button.btn-danger',
    ) as HTMLButtonElement;
    await act(async () => confirmBtn.click());
    await settle();

    // Hộp thoại vẫn mở và hiển thị thông báo lỗi 409.
    expect(container.querySelector('[role="dialog"]')).not.toBeNull();
    expect(container.textContent).toContain('Không thể hủy đơn ở trạng thái này.');
    // Dữ liệu mới phải thay thế trạng thái cũ trước khi khách thử hủy lần nữa.
    expect(api.calls.filter((call) => call.method === 'GET')).toHaveLength(2);
    expect(container.querySelector('.order-badge')?.textContent).toBe('Đã xác nhận');
    const keepButton = container.querySelector(
      '.dialog-foot button.btn-outline',
    ) as HTMLButtonElement;
    await act(async () => keepButton.click());
    expect(
      Array.from(container.querySelectorAll('button')).some((button) =>
        button.textContent?.includes('Hủy đơn hàng'),
      ),
    ).toBe(false);
  });
});
