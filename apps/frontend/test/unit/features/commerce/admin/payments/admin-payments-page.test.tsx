// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import {
  actorWith,
  clickButton,
  createHarness,
  findField,
  queryOf,
  selectOption,
  waitFor,
  type AdminListHarness,
} from '../../../identity/admin/admin-list-test-kit';

let harness: AdminListHarness;

afterEach(async () => {
  await harness.cleanup();
});

const TRANSACTIONS_PATH = '/api/admin/payments/transactions';

function transaction(id: string, overrides: Record<string, unknown> = {}) {
  return {
    id,
    providerTxId: `SP-${id}`,
    amount: 500000,
    content: `CK ${id}`,
    matchStatus: 'unmatched',
    receivedAt: '2026-10-12T03:30:00Z',
    order: null,
    ...overrides,
  };
}

function matchedTransaction(id: string) {
  return transaction(id, {
    matchStatus: 'matched',
    order: {
      code: 'PK-1',
      status: 'pending_confirmation',
      paymentStatus: 'paid',
      total: 500000,
      flags: ['overpaid'],
    },
  });
}

function page(
  items: unknown[],
  total = items.length,
  paging: { page: number; limit: number } = { page: 1, limit: 20 },
) {
  return { items, total, ...paging };
}

function codOrder(code: string) {
  return {
    code,
    createdAt: '2026-10-10T03:30:00Z',
    status: 'delivered',
    paymentStatus: 'unpaid',
    paymentMethod: 'cod',
    total: 750000,
    flags: [] as string[],
    recipientName: 'Lê Văn Cường',
    recipientPhone: '0912000111',
    lineCount: 1,
  };
}

function orderDetail(code: string) {
  return {
    code,
    status: 'pending_confirmation',
    paymentStatus: 'unpaid',
    paymentMethod: 'sepay',
    total: 500000,
    createdAt: '2026-10-12T03:30:00Z',
    flags: [],
  };
}

interface Scenario {
  url?: string;
  permissions?: string[];
  transactions?: unknown[];
  codOrders?: unknown[];
  // Phân trang response của từng bảng. Hai bảng có thể trả `page` và `limit` khác nhau.
  transactionsPaging?: { page: number; limit: number };
  codPaging?: { page: number; limit: number };
  // Response cho `PATCH` giao dịch. Mặc định trả giao dịch đã khớp.
  onPatch?: (init?: RequestInit) => unknown;
  // Response cho `GET /api/admin/orders/{mã}` khi tìm mã đơn.
  onLookup?: (code: string) => unknown;
  onCodReceipt?: () => unknown;
}

const READ_ONLY = ['payments:read', 'orders:read'];
const RECONCILER = ['payments:read', 'payments:reconcile', 'orders:read'];

function open(scenario: Scenario = {}) {
  const {
    url = '/admin/payments',
    permissions = RECONCILER,
    transactions = [transaction('tx-1'), matchedTransaction('tx-2')],
    codOrders = [codOrder('PK-COD-1')],
    transactionsPaging,
    codPaging,
    onPatch,
    onLookup,
    onCodReceipt,
  } = scenario;
  harness = createHarness(actorWith(permissions), (requested, init) => {
    const method = init?.method ?? 'GET';
    const path = new URL(requested, 'http://localhost').pathname;
    if (path === TRANSACTIONS_PATH && method === 'GET') {
      return page(transactions, transactions.length, transactionsPaging);
    }
    if (path.startsWith(`${TRANSACTIONS_PATH}/`) && method === 'PATCH') {
      return onPatch ? onPatch(init) : transaction('tx-1', { matchStatus: 'matched' });
    }
    if (path === '/api/admin/orders' && method === 'GET') {
      return page(codOrders, codOrders.length, codPaging);
    }
    if (path.endsWith('/cod-receipt') && method === 'PUT') {
      return onCodReceipt ? onCodReceipt() : { code: 'PK-COD-1' };
    }
    if (path.startsWith('/api/admin/orders/') && method === 'GET') {
      const code = decodeURIComponent(path.split('/').pop()!);
      return onLookup ? onLookup(code) : orderDetail(code);
    }
    return [];
  });
  return harness.render(url);
}

function text() {
  return harness.container.textContent ?? '';
}

function tab(label: string) {
  return [...harness.container.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find(
    (element) => element.textContent === label,
  );
}

function rowOf(content: string) {
  return [...harness.container.querySelectorAll('tbody tr')].find((row) =>
    row.textContent?.includes(content),
  )!;
}

function buttonIn(root: ParentNode, label: string) {
  return [...root.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === label,
  ) as HTMLButtonElement | undefined;
}

function dialog() {
  return harness.container.querySelector('[role="dialog"]');
}

function failure(status: number, code: string, message: string) {
  return new Response(JSON.stringify({ error: { code, message } }), { status });
}

async function waitForRows() {
  await waitFor(() => harness.container.querySelectorAll('tbody tr').length > 0, 'danh sách');
}

function transactionRequests() {
  return harness.requestedUrls.filter((url) => url.startsWith(TRANSACTIONS_PATH));
}

// React theo dõi giá trị ô qua setter gốc. Gán trực tiếp `.value` không kích hoạt `onChange`.
async function typeInto(selector: string, value: string) {
  const element = dialog()!.querySelector<HTMLInputElement>(selector)!;
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(element, value);
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

describe('màn đối soát: tab SePay', () => {
  it('mặc định gửi matchStatus=unmatched', async () => {
    await open();
    await waitFor(() => transactionRequests().length > 0, 'request giao dịch');

    expect(queryOf(transactionRequests()[0]!)).toEqual({ matchStatus: 'unmatched' });
    expect(findField(harness.container, 'Trạng thái khớp')!.value).toBe('unmatched');
  });

  it('chọn Tất cả thì không gửi matchStatus', async () => {
    await open();
    await waitFor(() => transactionRequests().length > 0, 'request đầu');

    await selectOption(findField(harness.container, 'Trạng thái khớp')!, 'all');
    await waitFor(() => transactionRequests().length > 1, 'request sau khi đổi');

    expect(queryOf(transactionRequests().at(-1)!)).toEqual({});
  });

  it('chọn cờ đơn gửi orderFlag', async () => {
    await open();
    await waitFor(() => transactionRequests().length > 0, 'request đầu');

    await selectOption(findField(harness.container, 'Ghi chú')!, 'overpaid');
    await waitFor(() => transactionRequests().length > 1, 'request sau khi đổi');

    expect(queryOf(transactionRequests().at(-1)!)).toEqual({
      matchStatus: 'unmatched',
      orderFlag: 'overpaid',
    });
  });

  it('hiện các cột của giao dịch, mã đơn gắn kèm là liên kết sang chi tiết', async () => {
    await open({ url: '/admin/payments?matchStatus=all' });
    await waitForRows();

    const matched = rowOf('SP-tx-2');
    expect(matched.textContent).toContain('10:30 12/10/2026');
    expect(matched.textContent).toContain('500.000');
    expect(matched.textContent).toContain('Đã khớp');
    expect(matched.textContent).toContain('Thanh toán dư');
    expect(matched.querySelector('a')!.getAttribute('href')).toBe('/admin/orders/PK-1');
    expect(rowOf('SP-tx-1').textContent).toContain('Chưa khớp');
  });

  it('dòng chưa khớp có hai nút khi có payments:reconcile, dòng đã khớp không có nút', async () => {
    await open({ url: '/admin/payments?matchStatus=all' });
    await waitForRows();

    expect(buttonIn(rowOf('SP-tx-1'), 'Gắn vào đơn')).toBeDefined();
    expect(buttonIn(rowOf('SP-tx-1'), 'Bỏ qua')).toBeDefined();
    expect(rowOf('SP-tx-2').querySelectorAll('button')).toHaveLength(0);
  });

  it('người chỉ có payments:read thấy danh sách nhưng không thấy nút', async () => {
    await open({ permissions: READ_ONLY });
    await waitForRows();

    expect(text()).toContain('SP-tx-1');
    expect(harness.container.querySelectorAll('tbody button')).toHaveLength(0);
  });

  it('người không có payments:read thấy thông báo không có quyền và API không bị gọi', async () => {
    await open({ permissions: ['orders:read'] });
    await waitFor(() => text().includes('payments:read'), 'thông báo không có quyền');

    expect(transactionRequests()).toHaveLength(0);
  });

  it('gắn vào đơn: mã không tồn tại hiện lỗi và nút Gắn tắt', async () => {
    await open({
      onLookup: () => failure(404, 'ORDER_NOT_FOUND', 'Không tìm thấy đơn hàng.'),
    });
    await waitForRows();

    await act(async () => buttonIn(rowOf('SP-tx-1'), 'Gắn vào đơn')!.click());
    await typeInto('input', 'PK-NONE');
    await act(async () => buttonIn(dialog()!, 'Tìm')!.click());
    await waitFor(() => dialog()!.textContent!.includes('Không tìm thấy đơn hàng.'), 'lỗi tìm mã');

    expect(buttonIn(dialog()!, 'Gắn')!.disabled).toBe(true);
    expect(harness.writes).toHaveLength(0);
  });

  it('nút Gắn tắt khi chưa tìm đơn', async () => {
    await open();
    await waitForRows();

    await act(async () => buttonIn(rowOf('SP-tx-1'), 'Gắn vào đơn')!.click());

    expect(buttonIn(dialog()!, 'Gắn')!.disabled).toBe(true);
  });

  it('gắn vào đơn: tìm mã có thật hiện tóm tắt, bấm Gắn gửi PATCH với orderCode', async () => {
    await open();
    await waitForRows();

    await act(async () => buttonIn(rowOf('SP-tx-1'), 'Gắn vào đơn')!.click());
    await typeInto('input', 'pk-2002');
    await act(async () => buttonIn(dialog()!, 'Tìm')!.click());
    await waitFor(() => dialog()!.textContent!.includes('PK-2002'), 'tóm tắt đơn');

    expect(dialog()!.textContent).toContain('Chờ xác nhận');
    expect(dialog()!.textContent).toContain('500.000');
    expect(harness.requestedUrls).toContain('/api/admin/orders/PK-2002');

    await act(async () => buttonIn(dialog()!, 'Gắn')!.click());
    await waitFor(() => harness.writes.length > 0, 'request gắn');

    expect(harness.writes[0]).toEqual({
      method: 'PATCH',
      url: `${TRANSACTIONS_PATH}/tx-1`,
      body: { orderCode: 'PK-2002' },
    });
    await waitFor(() => dialog() === null, 'đóng hộp thoại');
    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Đã gắn giao dịch vào đơn PK-2002.',
    );
  });

  it('lỗi 409 khi gắn giữ hộp thoại mở và hiện thông báo của API', async () => {
    const message = 'Giao dịch đã được gắn vào đơn khác.';
    await open({ onPatch: () => failure(409, 'TRANSACTION_ALREADY_MATCHED', message) });
    await waitForRows();

    await act(async () => buttonIn(rowOf('SP-tx-1'), 'Gắn vào đơn')!.click());
    await typeInto('input', 'PK-2002');
    await act(async () => buttonIn(dialog()!, 'Tìm')!.click());
    await waitFor(() => dialog()!.textContent!.includes('PK-2002'), 'tóm tắt đơn');
    await act(async () => buttonIn(dialog()!, 'Gắn')!.click());
    await waitFor(() => dialog()!.querySelector('[role="alert"]') !== null, 'lỗi 409');

    expect(dialog()!.querySelector('[role="alert"]')!.textContent).toContain(message);
    expect(dialog()).not.toBeNull();
  });

  it('bỏ qua: xác nhận rồi gửi PATCH với matchStatus ignored', async () => {
    await open();
    await waitForRows();

    await act(async () => buttonIn(rowOf('SP-tx-1'), 'Bỏ qua')!.click());
    expect(dialog()!.textContent).toContain('không thuộc đơn nào');
    expect(harness.writes).toHaveLength(0);

    await act(async () => buttonIn(dialog()!, 'Bỏ qua')!.click());
    await waitFor(() => harness.writes.length > 0, 'request bỏ qua');

    expect(harness.writes[0]).toEqual({
      method: 'PATCH',
      url: `${TRANSACTIONS_PATH}/tx-1`,
      body: { matchStatus: 'ignored' },
    });
  });

  it('thành công đánh dấu cũ danh sách giao dịch để tải lại', async () => {
    await open();
    await waitForRows();
    const before = transactionRequests().length;

    await act(async () => buttonIn(rowOf('SP-tx-1'), 'Bỏ qua')!.click());
    await act(async () => buttonIn(dialog()!, 'Bỏ qua')!.click());
    await waitFor(() => dialog() === null, 'đóng hộp thoại');
    await waitFor(() => transactionRequests().length > before, 'tải lại danh sách');

    expect(transactionRequests().length).toBeGreaterThan(before);
  });
});

describe('màn đối soát: tab COD', () => {
  it('gửi đúng ba bộ lọc cố định của đơn COD đã giao chưa nhận tiền', async () => {
    await open({ url: '/admin/payments?tab=cod' });
    await waitFor(
      () => harness.requestedUrls.some((url) => url.startsWith('/api/admin/orders?')),
      'request đơn COD',
    );

    const request = harness.requestedUrls.find((url) => url.startsWith('/api/admin/orders?'))!;
    expect(queryOf(request)).toEqual({
      status: 'delivered',
      paymentMethod: 'cod',
      paymentStatus: 'unpaid',
    });
  });

  it('hiện mã đơn là liên kết, người nhận và tổng tiền', async () => {
    await open({ url: '/admin/payments?tab=cod' });
    await waitForRows();

    const row = rowOf('PK-COD-1');
    expect(row.querySelector('a')!.getAttribute('href')).toBe('/admin/orders/PK-COD-1');
    expect(row.textContent).toContain('Lê Văn Cường');
    expect(row.textContent).toContain('750.000');
  });

  it('nút Đã nhận tiền COD chỉ hiện khi có payments:reconcile', async () => {
    await open({ url: '/admin/payments?tab=cod', permissions: READ_ONLY });
    await waitForRows();

    expect(buttonIn(rowOf('PK-COD-1'), 'Đã nhận tiền COD')).toBeUndefined();
  });

  it('xác nhận gọi cod-receipt của đơn', async () => {
    await open({ url: '/admin/payments?tab=cod' });
    await waitForRows();

    await act(async () => buttonIn(rowOf('PK-COD-1'), 'Đã nhận tiền COD')!.click());
    expect(dialog()).not.toBeNull();
    expect(harness.writes).toHaveLength(0);
    await act(async () => buttonIn(dialog()!, 'Ghi nhận')!.click());
    await waitFor(() => harness.writes.length > 0, 'request cod-receipt');

    expect(harness.writes[0]).toEqual({
      method: 'PUT',
      url: '/api/admin/orders/PK-COD-1/cod-receipt',
      body: null,
    });
    await waitFor(() => dialog() === null, 'đóng hộp thoại');
    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Đã ghi nhận nhận tiền COD của đơn PK-COD-1.',
    );
  });

  it('danh sách COD rỗng hiện thông báo riêng', async () => {
    await open({ url: '/admin/payments?tab=cod', codOrders: [] });
    await waitFor(() => text().includes('Không có đơn COD nào đang chờ nhận tiền.'), 'rỗng');

    expect(text()).toContain('Không có đơn COD nào đang chờ nhận tiền.');
  });
});

describe('màn đối soát: tab trên URL', () => {
  it('mặc định chọn tab SePay', async () => {
    await open();
    await waitFor(() => tab('SePay') !== undefined, 'dải tab');

    expect(tab('SePay')!.getAttribute('aria-selected')).toBe('true');
    expect(tab('COD')!.getAttribute('aria-selected')).toBe('false');
  });

  it('mở link tab=cod chọn tab COD', async () => {
    await open({ url: '/admin/payments?tab=cod' });
    await waitFor(() => tab('COD') !== undefined, 'dải tab');

    expect(tab('COD')!.getAttribute('aria-selected')).toBe('true');
  });

  it('bấm tab COD đổi danh sách sang đơn COD', async () => {
    await open();
    await waitFor(() => tab('COD') !== undefined, 'dải tab');

    await clickButton(harness.container, 'COD');
    await waitFor(
      () => harness.requestedUrls.some((url) => url.startsWith('/api/admin/orders?')),
      'request đơn COD',
    );

    expect(tab('COD')!.getAttribute('aria-selected')).toBe('true');
  });

  it('mũi tên phải chuyển focus sang tab COD', async () => {
    await open();
    await waitFor(() => tab('SePay') !== undefined, 'dải tab');
    tab('SePay')!.focus();

    await act(async () => {
      tab('SePay')!.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }),
      );
    });

    expect(document.activeElement).toBe(tab('COD'));
  });

  it('người không có orders:read không thấy tab COD', async () => {
    await open({ permissions: ['payments:read'] });
    await waitFor(() => tab('SePay') !== undefined, 'dải tab');

    expect(tab('COD')).toBeUndefined();
  });
});

describe('màn đối soát có cột STT', () => {
  const transactionList = [transaction('tx-1'), transaction('tx-2')];
  const codList = [codOrder('PK-COD-1'), codOrder('PK-COD-2')];

  function rowNumbers() {
    return [...harness.container.querySelectorAll('tbody tr')].map(
      (row) => row.firstElementChild!.textContent,
    );
  }

  describe('tab SePay', () => {
    it('cột đầu có tiêu đề STT, tiêu đề và ô đều có class num', async () => {
      await open({ transactions: transactionList });
      await waitForRows();

      const headerCell = harness.container.querySelector('thead th')!;
      expect(headerCell.textContent).toBe('STT');
      expect(headerCell.getAttribute('scope')).toBe('col');
      expect(headerCell.classList.contains('num')).toBe(true);
      expect(headerCell.classList.contains('col-row-number')).toBe(true);
      const firstCell = harness.container.querySelector('tbody tr')!.firstElementChild!;
      expect(firstCell.tagName).toBe('TD');
      expect(firstCell.classList.contains('num')).toBe(true);
      expect(harness.container.querySelector('tbody tr th[scope="row"]')).not.toBeNull();
    });

    it('response page 2, limit 20 thì STT là 21 và 22', async () => {
      await open({ transactions: transactionList, transactionsPaging: { page: 2, limit: 20 } });
      await waitForRows();

      expect(rowNumbers()).toEqual(['21', '22']);
    });

    it('response page 3, limit 5 thì STT bắt đầu từ 11, không theo hằng số của màn', async () => {
      await open({ transactions: transactionList, transactionsPaging: { page: 3, limit: 5 } });
      await waitForRows();

      expect(rowNumbers()).toEqual(['11', '12']);
    });
  });

  describe('tab COD', () => {
    it('cột đầu có tiêu đề STT, tiêu đề và ô đều có class num', async () => {
      await open({ url: '/admin/payments?tab=cod', codOrders: codList });
      await waitForRows();

      const headerCell = harness.container.querySelector('thead th')!;
      expect(headerCell.textContent).toBe('STT');
      expect(headerCell.getAttribute('scope')).toBe('col');
      expect(headerCell.classList.contains('num')).toBe(true);
      expect(headerCell.classList.contains('col-row-number')).toBe(true);
      const firstCell = harness.container.querySelector('tbody tr')!.firstElementChild!;
      expect(firstCell.tagName).toBe('TD');
      expect(firstCell.classList.contains('num')).toBe(true);
      expect(harness.container.querySelector('tbody tr th[scope="row"]')).not.toBeNull();
    });

    it('response page 2, limit 20 thì STT là 21 và 22', async () => {
      await open({
        url: '/admin/payments?tab=cod',
        codOrders: codList,
        codPaging: { page: 2, limit: 20 },
      });
      await waitForRows();

      expect(rowNumbers()).toEqual(['21', '22']);
    });

    it('response page 3, limit 5 thì STT bắt đầu từ 11, không theo hằng số của màn', async () => {
      await open({
        url: '/admin/payments?tab=cod',
        codOrders: codList,
        codPaging: { page: 3, limit: 5 },
      });
      await waitForRows();

      expect(rowNumbers()).toEqual(['11', '12']);
    });

    it('sau khi xem tab SePay, bảng COD vẫn đánh số theo response COD', async () => {
      // React Query giữ dữ liệu giao dịch trong bộ nhớ khi chuyển tab. Bảng COD không được lấy `page` và `limit` của dữ liệu đó.
      await open({
        transactions: transactionList,
        transactionsPaging: { page: 3, limit: 5 },
        codOrders: codList,
        codPaging: { page: 2, limit: 20 },
      });
      await waitForRows();
      expect(rowNumbers()).toEqual(['11', '12']);

      await act(async () => tab('COD')!.click());
      await waitFor(() => harness.container.querySelector('table.cod-table') !== null, 'bảng COD');

      expect(rowNumbers()).toEqual(['21', '22']);
    });

    it('quay lại tab SePay, bảng giao dịch vẫn đánh số theo response giao dịch', async () => {
      await open({
        url: '/admin/payments?tab=cod',
        transactions: transactionList,
        transactionsPaging: { page: 3, limit: 5 },
        codOrders: codList,
        codPaging: { page: 2, limit: 20 },
      });
      await waitForRows();
      expect(rowNumbers()).toEqual(['21', '22']);

      await act(async () => tab('SePay')!.click());
      await waitFor(
        () => harness.container.querySelector('table.payment-table') !== null,
        'bảng giao dịch',
      );

      expect(rowNumbers()).toEqual(['11', '12']);
    });
  });
});
