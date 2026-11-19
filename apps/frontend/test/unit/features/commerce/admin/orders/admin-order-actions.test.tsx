// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { findCommerceExample } from '../../contract-fixtures';
import { exampleData, exampleError } from '../../fixture-response';
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

const START_LABEL = 'Giao hàng thủ công';
const START_SUBMIT = 'Bắt đầu giao thủ công';
const RETURN_LABEL = 'Quay về đã xác nhận';
const REASON_ERROR = 'Nhập lý do từ 1 đến 500 ký tự.';
const FULFILL_PERMISSIONS = ['orders:read', 'orders:fulfill'];

// Đơn trong response mẫu mang mã riêng của file mẫu. Đổi về mã của màn để màn vẫn tìm thấy đơn.
function fixtureOrder(exampleName: string) {
  const example = findCommerceExample('manual-fulfillment.json', exampleName);
  return { ...exampleData(example), code: 'PK-1001' };
}

function fixtureError(exampleName: string) {
  const example = findCommerceExample('manual-fulfillment.json', exampleName);
  return new Response(JSON.stringify(example.response), { status: example.status });
}

function detailReadCount() {
  return harness.requestedUrls.filter((url) => url === ORDER_PATH).length;
}

// Chờ một nhịp ngắn để một lần tải lại sai (nếu có) kịp xảy ra trước khi kiểm.
async function settle() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 100));
  });
}

// Hai thao tác giao thủ công. Mỗi mục có đơn ban đầu, nhãn nút và trạng thái đích gửi lên API.
const FULFILLMENT_CASES = [
  {
    name: 'giao hàng thủ công',
    initial: () => order({ status: 'confirmed' }),
    openLabel: START_LABEL,
    submitLabel: START_SUBMIT,
    toStatus: 'shipping',
    successMessage: 'Đã chuyển đơn sang giao hàng thủ công.',
  },
  {
    name: 'quay về đã xác nhận',
    initial: () => order({ status: 'shipping', flags: ['manual_shipping'] }),
    openLabel: RETURN_LABEL,
    submitLabel: RETURN_LABEL,
    toStatus: 'confirmed',
    successMessage: 'Đã đưa đơn về đã xác nhận.',
  },
];

describe('nút giao hàng thủ công trên chi tiết đơn', () => {
  it('đơn confirmed xếp nút Hủy đơn, Giao hàng thủ công rồi Tạo vận đơn từ trái sang phải', async () => {
    await open({
      initial: order({ status: 'confirmed' }),
      permissions: [...FULFILL_PERMISSIONS, 'orders:cancel'],
    });
    await waitForOrder();

    const labels = [...actionBar().querySelectorAll('button')].map((button) => button.textContent);
    expect(labels).toEqual(['Hủy đơn', START_LABEL, 'Tạo vận đơn']);
  });

  it('Giao hàng thủ công là nút viền, còn Tạo vận đơn là nút chính duy nhất', async () => {
    await open({
      initial: order({ status: 'confirmed' }),
      permissions: [...FULFILL_PERMISSIONS, 'orders:cancel'],
    });
    await waitForOrder();

    expect(buttonIn(actionBar(), START_LABEL)!.className).toBe('btn btn-outline');
    expect(buttonIn(actionBar(), 'Tạo vận đơn')!.className).toBe('btn btn-primary');
    expect(buttonIn(actionBar(), 'Hủy đơn')!.className).toBe('btn btn-danger');
    expect(actionBar().querySelectorAll('.btn-primary')).toHaveLength(1);
  });

  it('đơn shipping có cờ manual_shipping hiện nút quay về dạng nút viền', async () => {
    await open({
      initial: order({ status: 'shipping', flags: ['manual_shipping'] }),
      permissions: FULFILL_PERMISSIONS,
    });
    await waitForOrder();

    expect(buttonIn(actionBar(), RETURN_LABEL)!.className).toBe('btn btn-outline');
  });

  it('người chỉ có orders:read không thấy hai nút mới', async () => {
    await open({ initial: order({ status: 'confirmed' }), permissions: ['orders:read'] });
    await waitForOrder();

    expect(buttonIn(actionBar(), START_LABEL)).toBeUndefined();
    expect(buttonIn(actionBar(), RETURN_LABEL)).toBeUndefined();
  });

  it('bấm nút giao thủ công mở hộp thoại có mô tả hậu quả và ô Lý do', async () => {
    await open({ initial: order({ status: 'confirmed' }), permissions: FULFILL_PERMISSIONS });
    await waitForOrder();

    await openAction(START_LABEL);

    expect(dialog()!.textContent).toContain('shop tự giao');
    expect(dialog()!.textContent).toContain('không tạo vận đơn GHN');
    const label = dialog()!.querySelector('label[for="fulfillment-reason"]');
    expect(label!.textContent).toBe('Lý do');
    expect(dialog()!.querySelector('textarea#fulfillment-reason')).not.toBeNull();
  });

  it('bấm nút quay về mở hộp thoại nói rõ cờ giao thủ công bị bỏ', async () => {
    await open({
      initial: order({ status: 'shipping', flags: ['manual_shipping'] }),
      permissions: FULFILL_PERMISSIONS,
    });
    await waitForOrder();

    await openAction(RETURN_LABEL);

    expect(dialog()!.textContent).toContain('Cờ giao thủ công của đơn bị bỏ');
    expect(dialog()!.querySelector('textarea#fulfillment-reason')).not.toBeNull();
  });

  it.each(FULFILLMENT_CASES)(
    '$name: bộ đếm ký tự đếm sau khi cắt khoảng trắng',
    async (scenario) => {
      await open({ initial: scenario.initial(), permissions: FULFILL_PERMISSIONS });
      await waitForOrder();

      await openAction(scenario.openLabel);
      await setValue('textarea', '  abc  ');

      expect(dialog()!.textContent).toContain('3/500 ký tự');
    },
  );

  it.each(FULFILLMENT_CASES)(
    '$name: lý do trống không gọi API và hiện lỗi dưới ô',
    async (scenario) => {
      await open({ initial: scenario.initial(), permissions: FULFILL_PERMISSIONS });
      await waitForOrder();

      await openAction(scenario.openLabel);
      await submitDialog(scenario.submitLabel);

      expect(harness.writes).toHaveLength(0);
      expect(dialog()!.textContent).toContain(REASON_ERROR);
      expect(dialog()!.querySelector('textarea')!.getAttribute('aria-invalid')).toBe('true');
    },
  );

  it.each(FULFILLMENT_CASES)('$name: lý do 501 ký tự không gọi API', async (scenario) => {
    await open({ initial: scenario.initial(), permissions: FULFILL_PERMISSIONS });
    await waitForOrder();

    await openAction(scenario.openLabel);
    await setValue('textarea', 'a'.repeat(501));
    await submitDialog(scenario.submitLabel);

    expect(harness.writes).toHaveLength(0);
    expect(dialog()!.textContent).toContain(REASON_ERROR);
  });

  it.each(FULFILLMENT_CASES)(
    '$name: gửi đúng một request PUT fulfillment với lý do đã cắt',
    async (scenario) => {
      await open({
        initial: scenario.initial(),
        permissions: FULFILL_PERMISSIONS,
        onWrite: () => fixtureOrder('start manual shipping'),
      });
      await waitForOrder();

      await openAction(scenario.openLabel);
      await setValue('textarea', '  GHN không nhận vận đơn.  ');
      await submitDialog(scenario.submitLabel);
      await waitFor(() => dialog() === null, 'đóng hộp thoại');

      expect(harness.writes).toEqual([
        {
          method: 'PUT',
          url: `${ORDER_PATH}/fulfillment`,
          body: { toStatus: scenario.toStatus, reason: 'GHN không nhận vận đơn.' },
        },
      ]);
    },
  );

  it.each(FULFILLMENT_CASES)(
    '$name: nút gửi khóa trong lúc chờ và bấm hai lần chỉ gửi một request',
    async (scenario) => {
      let release: (value: unknown) => void = () => undefined;
      const pending = new Promise((resolve) => {
        release = resolve;
      });
      await open({
        initial: scenario.initial(),
        permissions: FULFILL_PERMISSIONS,
        onWrite: () => pending,
      });
      await waitForOrder();

      await openAction(scenario.openLabel);
      await setValue('textarea', 'Lý do hợp lệ');
      await submitDialog(scenario.submitLabel);

      const submit = dialog()!.querySelector<HTMLButtonElement>('button[type="submit"]')!;
      expect(submit.disabled).toBe(true);
      await act(async () => submit.click());
      expect(harness.writes).toHaveLength(1);

      release(fixtureOrder('start manual shipping'));
      await waitFor(() => dialog() === null, 'đóng hộp thoại');
    },
  );

  it.each(FULFILLMENT_CASES)(
    '$name: thành công ghi chi tiết vào cache, đánh dấu danh sách cũ và báo thành công',
    async (scenario) => {
      const updated = fixtureOrder('start manual shipping');
      await open({
        initial: scenario.initial(),
        permissions: FULFILL_PERMISSIONS,
        onWrite: () => updated,
      });
      await waitForOrder();
      // Đăng ký một truy vấn danh sách để kiểm việc đánh dấu cũ.
      await harness.client.prefetchQuery({
        queryKey: ['admin-orders', 'kiểm tra'],
        queryFn: () => [],
      });

      await openAction(scenario.openLabel);
      await setValue('textarea', 'Lý do hợp lệ');
      await submitDialog(scenario.submitLabel);
      await waitFor(() => dialog() === null, 'đóng hộp thoại');

      expect(harness.client.getQueryData(['admin-order', 'PK-1001'])).toEqual(updated);
      expect(harness.client.getQueryState(['admin-orders', 'kiểm tra'])!.isInvalidated).toBe(true);
      expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
        scenario.successMessage,
      );
    },
  );

  it.each(FULFILLMENT_CASES)(
    '$name: lỗi 409 giữ hộp thoại, giữ lý do và tải lại chi tiết đơn',
    async (scenario) => {
      const apiError = exampleError(
        findCommerceExample('manual-fulfillment.json', 'invalid transition'),
      );
      await open({
        initial: scenario.initial(),
        permissions: FULFILL_PERMISSIONS,
        onWrite: () => fixtureError('invalid transition'),
      });
      await waitForOrder();
      expect(detailReadCount()).toBe(1);

      await openAction(scenario.openLabel);
      await setValue('textarea', 'Lý do đã nhập');
      await submitDialog(scenario.submitLabel);
      await waitFor(() => dialog()!.textContent!.includes(apiError.message), 'câu lỗi của API');
      await waitFor(() => detailReadCount() === 2, 'tải lại chi tiết đơn');

      expect(dialog()).not.toBeNull();
      expect(dialog()!.querySelector('textarea')!.value).toBe('Lý do đã nhập');
    },
  );

  it.each(FULFILLMENT_CASES)(
    '$name: lỗi 400 giữ hộp thoại, giữ lý do và không tải lại chi tiết đơn',
    async (scenario) => {
      const apiError = exampleError(
        findCommerceExample('manual-fulfillment.json', 'invalid reason'),
      );
      await open({
        initial: scenario.initial(),
        permissions: FULFILL_PERMISSIONS,
        onWrite: () => fixtureError('invalid reason'),
      });
      await waitForOrder();

      await openAction(scenario.openLabel);
      await setValue('textarea', 'Lý do đã nhập');
      await submitDialog(scenario.submitLabel);
      await waitFor(() => dialog()!.querySelector('[role="alert"]') !== null, 'câu lỗi của API');
      await settle();

      expect(dialog()!.textContent).toContain(apiError.details![0]!.message);
      expect(dialog()!.querySelector('textarea')!.value).toBe('Lý do đã nhập');
      expect(detailReadCount()).toBe(1);
    },
  );

  it('thao tác cũ gặp lỗi 409 không tải lại chi tiết đơn', async () => {
    await open({ initial: order(), onWrite: () => conflict('Đơn đã đổi trạng thái.') });
    await waitForOrder();

    await openAction('Xác nhận đơn');
    await submitDialog('Xác nhận');
    await waitFor(() => dialog()!.querySelector('[role="alert"]') !== null, 'câu lỗi của API');
    await settle();

    expect(detailReadCount()).toBe(1);
  });
});

// Mỗi thao tác thuộc một trong ba kiểu nút: nguy hiểm, phụ hoặc chính. Đơn và quyền đủ để nút hiện ra.
const BUTTON_STYLE_CASES = [
  {
    label: 'Hủy đơn',
    className: 'btn btn-danger',
    initial: () => order(),
    permissions: ['orders:read', 'orders:cancel'],
  },
  {
    label: 'Hủy vận đơn',
    className: 'btn btn-danger',
    initial: () =>
      order({
        status: 'shipping',
        shipment: { id: 'shipment-1', trackingCode: 'GHN1', status: 'picking', fee: 30000 },
      }),
    permissions: FULFILL_PERMISSIONS,
  },
  {
    label: START_LABEL,
    className: 'btn btn-outline',
    initial: () => order({ status: 'confirmed' }),
    permissions: FULFILL_PERMISSIONS,
  },
  {
    label: RETURN_LABEL,
    className: 'btn btn-outline',
    initial: () => order({ status: 'shipping', flags: ['manual_shipping'] }),
    permissions: FULFILL_PERMISSIONS,
  },
  {
    label: 'Đã xuất hóa đơn',
    className: 'btn btn-outline',
    initial: () => order({ invoice: { companyName: 'ABC' } }),
    permissions: ['orders:read', 'orders:update-status'],
  },
  {
    label: 'Xác nhận đơn',
    className: 'btn btn-primary',
    initial: () => order(),
    permissions: ['orders:read', 'orders:update-status'],
  },
  {
    label: 'Tạo vận đơn',
    className: 'btn btn-primary',
    initial: () => order({ status: 'confirmed' }),
    permissions: FULFILL_PERMISSIONS,
  },
  {
    label: 'Đã nhận tiền COD',
    className: 'btn btn-primary',
    initial: () => order({ status: 'delivered', paymentMethod: 'cod', paymentStatus: 'unpaid' }),
    permissions: ['orders:read', 'payments:reconcile'],
  },
  {
    label: 'Đã hoàn tiền',
    className: 'btn btn-primary',
    initial: () =>
      order({ status: 'cancelled', paymentMethod: 'sepay', paymentStatus: 'refund_pending' }),
    permissions: ['orders:read', 'refunds:manage'],
  },
];

describe('kiểu nút của từng thao tác trên chi tiết đơn', () => {
  it.each(BUTTON_STYLE_CASES)('nút $label có lớp $className', async (scenario) => {
    await open({ initial: scenario.initial(), permissions: scenario.permissions });
    await waitForOrder();

    expect(buttonIn(actionBar(), scenario.label)!.className).toBe(scenario.className);
  });
});
