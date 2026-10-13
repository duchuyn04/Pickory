// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import {
  actorWith,
  createHarness,
  findButton,
  waitFor,
  type AdminListHarness,
} from '../../../identity/admin/admin-list-test-kit';

let harness: AdminListHarness;

afterEach(async () => {
  await harness.cleanup();
});

const ORDER_PATH = '/api/admin/orders/PK-1001';

function order(overrides: Record<string, unknown> = {}) {
  return {
    code: 'PK-1001',
    status: 'pending_confirmation',
    paymentStatus: 'unpaid',
    paymentMethod: 'cod',
    createdAt: '2026-10-12T03:30:00Z',
    paymentDeadline: null,
    flags: [] as string[],
    contact: { name: 'Nguyễn Văn An', phone: '0901234567', email: 'an@example.com' },
    shippingAddress: { line: '12 Lê Lợi' },
    invoice: null,
    items: [],
    subtotal: 500000,
    shippingFee: 30000,
    shippingDiscount: 0,
    total: 530000,
    statusHistory: [],
    shipment: null,
    suggestedParcel: null,
    refundTransactionRef: null,
    ...overrides,
  };
}

interface Scenario {
  initial: Record<string, unknown>;
  // Response của route ghi. Mặc định trả đơn đã đổi sang `confirmed`.
  onWrite?: (url: string, init?: RequestInit) => unknown;
  permissions?: string[];
}

function open({
  initial,
  onWrite,
  permissions = ['orders:read', 'orders:update-status'],
}: Scenario) {
  harness = createHarness(actorWith(permissions), (url, init) => {
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url === ORDER_PATH) return initial;
    if (method !== 'GET') return onWrite ? onWrite(url, init) : order({ status: 'confirmed' });
    return [];
  });
  return harness.render('/admin/orders/PK-1001');
}

function text() {
  return harness.container.textContent ?? '';
}

function actionBar() {
  return harness.container.querySelector('.order-actions')!;
}

async function waitForOrder() {
  await waitFor(() => text().includes('PK-1001'), 'chi tiết đơn');
}

function buttonIn(root: ParentNode, label: string) {
  return [...root.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === label,
  ) as HTMLButtonElement | undefined;
}

function dialog() {
  return harness.container.querySelector('[role="dialog"]');
}

async function openAction(label: string) {
  const button = buttonIn(actionBar(), label)!;
  button.focus();
  await act(async () => button.click());
  return button;
}

async function submitDialog(label: string) {
  await act(async () => buttonIn(dialog()!, label)!.click());
}

// React theo dõi giá trị ô qua setter gốc. Gán trực tiếp `.value` không kích hoạt `onChange`.
async function setValue(selector: string, value: string) {
  const element = dialog()!.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
  const prototype =
    element instanceof HTMLTextAreaElement
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
  await act(async () => {
    Object.getOwnPropertyDescriptor(prototype, 'value')!.set!.call(element, value);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

function conflict(message: string) {
  return new Response(JSON.stringify({ error: { code: 'ORDER_STATE_CONFLICT', message } }), {
    status: 409,
  });
}

describe('nút thao tác trên chi tiết đơn', () => {
  it('người chỉ có orders:read không thấy nút nào', async () => {
    await open({ initial: order(), permissions: ['orders:read'] });
    await waitForOrder();

    expect(actionBar().querySelectorAll('button')).toHaveLength(0);
  });

  it('sales thấy nút xác nhận với đơn chờ xác nhận', async () => {
    await open({ initial: order() });
    await waitForOrder();

    expect(buttonIn(actionBar(), 'Xác nhận đơn')).toBeDefined();
  });

  it('sales không thấy nút hoàn tiền với đơn refund_pending', async () => {
    await open({
      initial: order({
        status: 'cancelled',
        paymentStatus: 'refund_pending',
        paymentMethod: 'sepay',
      }),
      permissions: ['orders:read', 'orders:update-status', 'orders:cancel', 'payments:reconcile'],
    });
    await waitForOrder();

    expect(buttonIn(actionBar(), 'Đã hoàn tiền')).toBeUndefined();
  });

  it('người có refunds:manage thấy nút hoàn tiền với đơn refund_pending', async () => {
    await open({
      initial: order({
        status: 'cancelled',
        paymentStatus: 'refund_pending',
        paymentMethod: 'sepay',
      }),
      permissions: ['orders:read', 'refunds:manage'],
    });
    await waitForOrder();

    expect(buttonIn(actionBar(), 'Đã hoàn tiền')).toBeDefined();
  });

  it('xác nhận thành công gửi đúng route, đổi badge và hiện thông báo thành công', async () => {
    await open({ initial: order() });
    await waitForOrder();

    await openAction('Xác nhận đơn');
    expect(dialog()).not.toBeNull();
    await submitDialog('Xác nhận');
    await waitFor(() => harness.writes.length > 0, 'request xác nhận');
    await waitFor(() => dialog() === null, 'đóng hộp thoại');

    expect(harness.writes[0]).toEqual({
      method: 'POST',
      url: '/api/admin/orders/PK-1001/confirmation',
      body: null,
    });
    expect(text()).toContain('Đã xác nhận');
    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Đã xác nhận đơn.',
    );
  });

  it('sau khi thao tác thành công danh sách đơn được đánh dấu cũ để tải lại', async () => {
    await open({ initial: order() });
    await waitForOrder();

    await openAction('Xác nhận đơn');
    await submitDialog('Xác nhận');
    await waitFor(() => dialog() === null, 'đóng hộp thoại');

    // Màn chi tiết không gọi lại API chi tiết bằng GET, vì response ghi đã là chi tiết mới.
    const detailReads = harness.requestedUrls.filter((url) => url === ORDER_PATH);
    expect(detailReads).toHaveLength(1);
  });

  it('hủy đơn với lý do rỗng không gọi API và hiện lỗi dưới ô', async () => {
    await open({
      initial: order(),
      permissions: ['orders:read', 'orders:cancel'],
    });
    await waitForOrder();

    await openAction('Hủy đơn');
    await submitDialog('Xác nhận hủy đơn');

    expect(harness.writes).toHaveLength(0);
    expect(dialog()!.textContent).toContain('Nhập lý do hủy đơn.');
    expect(dialog()).not.toBeNull();
  });

  it('hủy đơn với lý do chỉ có khoảng trắng không gọi API', async () => {
    await open({ initial: order(), permissions: ['orders:read', 'orders:cancel'] });
    await waitForOrder();

    await openAction('Hủy đơn');
    await setValue('textarea', '    ');
    await submitDialog('Xác nhận hủy đơn');

    expect(harness.writes).toHaveLength(0);
  });

  it('hủy đơn với lý do hợp lệ gửi body có reason', async () => {
    await open({
      initial: order(),
      permissions: ['orders:read', 'orders:cancel'],
      onWrite: () => order({ status: 'cancelled' }),
    });
    await waitForOrder();

    await openAction('Hủy đơn');
    await setValue('textarea', 'Khách đổi ý');
    await submitDialog('Xác nhận hủy đơn');
    await waitFor(() => harness.writes.length > 0, 'request hủy');

    expect(harness.writes[0]).toEqual({
      method: 'POST',
      url: '/api/admin/orders/PK-1001/cancellation',
      body: { reason: 'Khách đổi ý' },
    });
  });

  it('tạo vận đơn điền sẵn bốn ô từ kiện gợi ý, sửa một ô rồi gửi đúng parcel', async () => {
    await open({
      initial: order({
        status: 'confirmed',
        suggestedParcel: { weightGrams: 1200, lengthCm: 30, widthCm: 20, heightCm: 10 },
      }),
      permissions: ['orders:read', 'orders:fulfill'],
      onWrite: () => order({ status: 'shipping' }),
    });
    await waitForOrder();

    await openAction('Tạo vận đơn');
    const inputs = [...dialog()!.querySelectorAll<HTMLInputElement>('input')];
    expect(inputs.map((input) => input.value)).toEqual(['1200', '30', '20', '10']);

    await setValue('#parcel-length', '35');
    await submitDialog('Tạo vận đơn');
    await waitFor(() => harness.writes.length > 0, 'request tạo vận đơn');

    expect(harness.writes[0]).toEqual({
      method: 'POST',
      url: '/api/admin/orders/PK-1001/shipment',
      body: { parcel: { weightGrams: 1200, lengthCm: 35, widthCm: 20, heightCm: 10 } },
    });
  });

  it('đơn không có kiện gợi ý thì bốn ô để trống', async () => {
    await open({
      initial: order({ status: 'confirmed' }),
      permissions: ['orders:read', 'orders:fulfill'],
    });
    await waitForOrder();

    await openAction('Tạo vận đơn');

    const inputs = [...dialog()!.querySelectorAll<HTMLInputElement>('input')];
    expect(inputs.map((input) => input.value)).toEqual(['', '', '', '']);
  });

  it.each(['0', 'abc'])('giá trị %s trong ô kiện hiện lỗi và không gọi API', async (value) => {
    await open({
      initial: order({
        status: 'confirmed',
        suggestedParcel: { weightGrams: 1200, lengthCm: 30, widthCm: 20, heightCm: 10 },
      }),
      permissions: ['orders:read', 'orders:fulfill'],
    });
    await waitForOrder();

    await openAction('Tạo vận đơn');
    await setValue('#parcel-weight', value);
    await submitDialog('Tạo vận đơn');

    expect(harness.writes).toHaveLength(0);
    expect(dialog()!.textContent).toContain('Nhập số nguyên dương.');
  });

  it('đã hoàn tiền yêu cầu mã giao dịch và gửi transactionRef', async () => {
    await open({
      initial: order({
        status: 'cancelled',
        paymentStatus: 'refund_pending',
        paymentMethod: 'sepay',
      }),
      permissions: ['orders:read', 'refunds:manage'],
      onWrite: () => order({ status: 'cancelled', paymentStatus: 'refunded' }),
    });
    await waitForOrder();

    await openAction('Đã hoàn tiền');
    await submitDialog('Ghi nhận hoàn tiền');
    expect(harness.writes).toHaveLength(0);
    expect(dialog()!.textContent).toContain('Nhập mã giao dịch hoàn tiền.');

    await setValue('input', 'FT26101200001');
    await submitDialog('Ghi nhận hoàn tiền');
    await waitFor(() => harness.writes.length > 0, 'request hoàn tiền');

    expect(harness.writes[0]).toEqual({
      method: 'PUT',
      url: '/api/admin/orders/PK-1001/refund',
      body: { transactionRef: 'FT26101200001' },
    });
  });

  it('lỗi 409 giữ hộp thoại mở và hiện thông báo của API', async () => {
    const message = 'Đơn đã đổi trạng thái, hãy tải lại trang.';
    await open({ initial: order(), onWrite: () => conflict(message) });
    await waitForOrder();

    await openAction('Xác nhận đơn');
    await submitDialog('Xác nhận');
    await waitFor(() => dialog()!.textContent!.includes(message), 'thông báo lỗi trong hộp thoại');

    expect(dialog()).not.toBeNull();
    expect(dialog()!.querySelector('[role="alert"]')!.textContent).toContain(message);
  });

  it('nút thực hiện tắt trong lúc chờ API', async () => {
    let release: (value: unknown) => void = () => undefined;
    const pending = new Promise((resolve) => {
      release = resolve;
    });
    await open({ initial: order(), onWrite: () => pending });
    await waitForOrder();

    await openAction('Xác nhận đơn');
    await submitDialog('Xác nhận');

    const submit = dialog()!.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(submit.disabled).toBe(true);
    // Bấm lần hai trong lúc chờ không gửi thêm request.
    await act(async () => submit.click());
    expect(harness.writes).toHaveLength(1);

    release(order({ status: 'confirmed' }));
    await waitFor(() => dialog() === null, 'đóng hộp thoại');
  });

  it('Escape đóng hộp thoại và trả focus về nút đã mở', async () => {
    await open({ initial: order() });
    await waitForOrder();

    const opener = await openAction('Xác nhận đơn');
    expect(dialog()).not.toBeNull();
    expect(dialog()!.contains(document.activeElement)).toBe(true);

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    });

    expect(dialog()).toBeNull();
    expect(document.activeElement).toBe(opener);
  });

  it('nút Hủy trong hộp thoại đóng mà không gọi API', async () => {
    await open({ initial: order() });
    await waitForOrder();

    await openAction('Xác nhận đơn');
    await act(async () => buttonIn(dialog()!, 'Hủy')!.click());

    expect(dialog()).toBeNull();
    expect(harness.writes).toHaveLength(0);
    expect(findButton(harness.container, 'Xác nhận đơn')).toBeDefined();
  });
});
