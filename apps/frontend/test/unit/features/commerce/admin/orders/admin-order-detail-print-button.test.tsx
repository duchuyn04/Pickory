// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import {
  actorWith,
  createHarness,
  waitFor,
  type AdminListHarness,
} from '../../../identity/admin/admin-list-test-kit';

let harness: AdminListHarness;

afterEach(async () => {
  await harness.cleanup();
});

function sampleOrder(overrides: Record<string, unknown> = {}) {
  return {
    code: 'PK-1001',
    status: 'delivered',
    paymentStatus: 'paid',
    paymentMethod: 'cod',
    createdAt: '2026-10-12T03:30:00Z',
    paymentDeadline: null,
    flags: [] as string[],
    contact: { name: 'Nguyễn Văn An', phone: '0901234567', email: 'an@example.com' },
    shippingAddress: {
      provinceCode: '79',
      provinceName: 'TP. Hồ Chí Minh',
      wardCode: '1',
      wardName: 'Phường Bến Nghé',
      line: '12 Lê Lợi',
    },
    invoice: null,
    items: [
      {
        skuId: 'sku-1',
        name: 'Áo thun',
        variantName: null,
        imagePublicId: null,
        imageUrl: null,
        quantity: 1,
        unitPrice: 250000,
        lineTotal: 250000,
      },
    ],
    subtotal: 250000,
    shippingFee: 30000,
    shippingDiscount: 0,
    total: 280000,
    shippingFeeSource: 'ghn',
    statusHistory: [],
    shipment: null,
    suggestedParcel: null,
    refundTransactionRef: null,
    ...overrides,
  };
}

function findPrintLink(): HTMLAnchorElement | undefined {
  return Array.from(harness.container.querySelectorAll('a')).find(
    (anchor) => anchor.textContent?.trim() === 'In hóa đơn',
  );
}

describe('nút In hóa đơn ở chi tiết đơn hàng', () => {
  it('hiện liên kết In hóa đơn trỏ tới trang in ở tab mới', async () => {
    harness = createHarness(actorWith(['orders:read']), () => sampleOrder());
    await harness.render('/admin/orders/PK-1001');
    await waitFor(
      () => harness.container.textContent?.includes('Đơn hàng PK-1001') ?? false,
      'tiêu đề đơn hàng',
    );

    const link = findPrintLink();
    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/admin/orders/print?codes=PK-1001');
    expect(link!.getAttribute('target')).toBe('_blank');
    const relTokens = (link!.getAttribute('rel') ?? '').split(/\s+/).filter(Boolean);
    expect(relTokens).toContain('noopener');
    expect(relTokens).toContain('noreferrer');
  });

  it('vẫn hiện nút In hóa đơn khi đơn ở trạng thái đã hủy', async () => {
    harness = createHarness(actorWith(['orders:read']), () => sampleOrder({ status: 'cancelled' }));
    await harness.render('/admin/orders/PK-1001');
    await waitFor(
      () => harness.container.textContent?.includes('Đã hủy') ?? false,
      'trạng thái đã hủy',
    );

    const link = findPrintLink();
    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/admin/orders/print?codes=PK-1001');
  });

  it('không hiện nút In hóa đơn khi đang nạp dữ liệu', async () => {
    harness = createHarness(actorWith(['orders:read']), () => new Promise(() => {}));
    await harness.render('/admin/orders/PK-1001');
    await waitFor(
      () => harness.container.textContent?.includes('Đang nạp chi tiết đơn hàng...') ?? false,
      'trạng thái đang nạp',
    );

    expect(findPrintLink()).toBeUndefined();
  });

  it('không hiện nút In hóa đơn khi API trả lỗi 404', async () => {
    harness = createHarness(
      actorWith(['orders:read']),
      () =>
        new Response(
          JSON.stringify({
            error: { code: 'ORDER_NOT_FOUND', message: 'Không tìm thấy đơn hàng.' },
          }),
          { status: 404 },
        ),
    );
    await harness.render('/admin/orders/PK-1001');
    await waitFor(
      () => harness.container.textContent?.includes('Không tìm thấy đơn hàng.') ?? false,
      'thông báo lỗi 404',
    );

    expect(findPrintLink()).toBeUndefined();
  });

  it('không hiện nút In hóa đơn khi thiếu quyền orders:read', async () => {
    harness = createHarness(actorWith(['products:read']), () => sampleOrder());
    await harness.render('/admin/orders/PK-1001');
    await waitFor(
      () => harness.container.textContent?.includes('orders:read') ?? false,
      'thông báo thiếu quyền',
    );

    expect(findPrintLink()).toBeUndefined();
  });

  it('không hiện nút In hóa đơn khi API trả lỗi 403', async () => {
    harness = createHarness(
      actorWith(['orders:read']),
      () =>
        new Response(
          JSON.stringify({ error: { code: 'FORBIDDEN', message: 'Không có quyền truy cập.' } }),
          { status: 403 },
        ),
    );
    await harness.render('/admin/orders/PK-1001');
    await waitFor(
      () => harness.container.textContent?.includes('Không có quyền truy cập.') ?? false,
      'thông báo lỗi 403',
    );

    expect(findPrintLink()).toBeUndefined();
  });
});
