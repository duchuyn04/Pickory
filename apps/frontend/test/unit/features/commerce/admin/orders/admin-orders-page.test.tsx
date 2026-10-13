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

function orderRow(code: string, overrides: Record<string, unknown> = {}) {
  return {
    code,
    createdAt: '2026-10-12T03:30:00Z',
    status: 'delivered',
    paymentStatus: 'paid',
    paymentMethod: 'cod',
    total: 1500000,
    flags: [] as string[],
    recipientName: 'Nguyễn Văn An',
    recipientPhone: '0901234567',
    lineCount: 2,
    ...overrides,
  };
}

function ordersPage(items: unknown[], total = items.length, page = 1) {
  return { items, total, page, limit: 20 };
}

function open(url: string, respond: (requested: string) => unknown, permissions = ['orders:read']) {
  harness = createHarness(actorWith(permissions), respond);
  return harness.render(url);
}

function orderRequests() {
  return harness.requestedUrls.filter((url) => url.startsWith('/api/admin/orders'));
}

function tab(label: string) {
  return [...harness.container.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find(
    (element) => element.textContent === label,
  )!;
}

const searchLabel = 'Tìm theo mã đơn, tên, số điện thoại hoặc email';

describe('màn danh sách đơn hàng của nhân viên', () => {
  it('mở link có bộ lọc thì gửi đúng tham số, tab và các ô hiện đúng giá trị', async () => {
    await open(
      '/admin/orders?status=delivered&paymentMethod=cod&paymentStatus=unpaid&search=PK',
      () => ordersPage([orderRow('PK-1')]),
    );
    await waitFor(() => orderRequests().length > 0, 'request đơn hàng');

    expect(queryOf(orderRequests()[0]!)).toEqual({
      status: 'delivered',
      paymentMethod: 'cod',
      paymentStatus: 'unpaid',
      search: 'PK',
    });
    expect(tab('Đã giao hàng').getAttribute('aria-selected')).toBe('true');
    expect(tab('Tất cả').getAttribute('aria-selected')).toBe('false');
    expect(findField(harness.container, searchLabel)!.value).toBe('PK');
    expect(findField(harness.container, 'Thanh toán')!.value).toBe('unpaid');
    expect(findField(harness.container, 'Phương thức')!.value).toBe('cod');
  });

  it('gửi cờ và khoảng ngày lên API khi có trên URL', async () => {
    await open('/admin/orders?flag=underpaid&from=2026-10-01&to=2026-10-12', () =>
      ordersPage([orderRow('PK-1')]),
    );
    await waitFor(() => orderRequests().length > 0, 'request đơn hàng');

    expect(queryOf(orderRequests()[0]!)).toEqual({
      flag: 'underpaid',
      from: '2026-10-01',
      to: '2026-10-12',
    });
    expect(findField(harness.container, 'Từ ngày')!.value).toBe('2026-10-01');
    expect(findField(harness.container, 'Đến ngày')!.value).toBe('2026-10-12');
  });

  it('hiện các cột của đơn với tiền VND, người nhận và liên kết sang chi tiết', async () => {
    await open('/admin/orders', () =>
      ordersPage([orderRow('PK-1', { flags: ['underpaid'], lineCount: 3 })]),
    );
    await waitFor(() => harness.container.querySelectorAll('tbody tr').length === 1, 'một dòng');

    const row = harness.container.querySelector('tbody tr')!;
    const link = row.querySelector('a')!;
    expect(link.textContent).toBe('PK-1');
    expect(link.getAttribute('href')).toBe('/admin/orders/PK-1');
    expect(row.textContent).toContain('Nguyễn Văn An');
    expect(row.textContent).toContain('0901234567');
    expect(row.textContent).toContain('1.500.000');
    expect(row.textContent).toContain('Đã giao hàng');
    expect(row.textContent).toContain('Đã thanh toán');
    expect(row.textContent).toContain('Thanh toán thiếu');
    expect(row.textContent).toContain('3');
  });

  it('bấm tab ghi status vào request và xóa page', async () => {
    await open('/admin/orders?page=3', () => ordersPage([orderRow('PK-1')], 100));
    await waitFor(() => orderRequests().length > 0, 'request đầu');
    expect(queryOf(orderRequests()[0]!).page).toBe('3');

    await clickButton(harness.container, 'Đang giao hàng');
    await waitFor(() => orderRequests().length > 1, 'request sau khi bấm tab');

    expect(queryOf(orderRequests().at(-1)!)).toEqual({ status: 'shipping' });
  });

  it('đổi ô chọn thanh toán ghi paymentStatus vào request', async () => {
    await open('/admin/orders', () => ordersPage([orderRow('PK-1')]));
    await waitFor(() => orderRequests().length > 0, 'request đầu');

    await selectOption(findField(harness.container, 'Thanh toán')!, 'refund_pending');
    await waitFor(() => orderRequests().length > 1, 'request sau khi đổi');

    expect(queryOf(orderRequests().at(-1)!)).toEqual({ paymentStatus: 'refund_pending' });
  });

  it('phím mũi tên phải chuyển focus sang tab kế tiếp và mũi tên trái quay lại', async () => {
    await open('/admin/orders', () => ordersPage([orderRow('PK-1')]));
    await waitFor(() => harness.container.querySelector('[role="tab"]') !== null, 'dải tab');
    const first = tab('Tất cả');
    first.focus();

    await act(async () => {
      first.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    });
    expect(document.activeElement).toBe(tab('Chờ thanh toán'));

    await act(async () => {
      tab('Chờ thanh toán').dispatchEvent(
        new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }),
      );
    });
    expect(document.activeElement).toBe(tab('Tất cả'));
  });

  it('chỉ tab đang chọn nằm trong thứ tự Tab của bàn phím', async () => {
    await open('/admin/orders?status=confirmed', () => ordersPage([orderRow('PK-1')]));
    await waitFor(() => harness.container.querySelector('[role="tab"]') !== null, 'dải tab');

    expect(tab('Đã xác nhận').tabIndex).toBe(0);
    expect(tab('Tất cả').tabIndex).toBe(-1);
  });

  it('lỗi 400 của API hiện thông báo tiếng Việt của API', async () => {
    const message = 'Ngày bắt đầu không được sau ngày kết thúc. (field: from)';
    await open(
      '/admin/orders?from=2026-10-12&to=2026-10-01',
      () =>
        new Response(JSON.stringify({ error: { code: 'VALIDATION_ERROR', message } }), {
          status: 400,
        }),
    );
    await waitFor(
      () => harness.container.querySelector('[role="alert"]') !== null,
      'thông báo lỗi',
    );

    expect(harness.container.querySelector('[role="alert"]')!.textContent).toContain(message);
  });

  it('danh sách rỗng khi có bộ lọc hiện thông báo không có kết quả phù hợp', async () => {
    await open('/admin/orders?search=zzz', () => ordersPage([]));
    await waitFor(
      () => harness.container.textContent!.includes('Không có kết quả phù hợp với bộ lọc.'),
      'thông báo rỗng',
    );

    expect(harness.container.querySelector('[role="status"]')!.textContent).toBe(
      'Không có kết quả phù hợp với bộ lọc.',
    );
  });

  it('danh sách rỗng khi không có bộ lọc giữ thông báo của màn', async () => {
    await open('/admin/orders', () => ordersPage([]));
    await waitFor(
      () => harness.container.textContent!.includes('Chưa có đơn hàng nào.'),
      'thông báo rỗng cũ',
    );

    expect(harness.container.textContent).not.toContain('Không có kết quả phù hợp với bộ lọc.');
  });

  it('người không có orders:read thấy thông báo không có quyền và không gọi API đơn', async () => {
    await open('/admin/orders', () => ordersPage([orderRow('PK-1')]), ['products:read']);
    await waitFor(
      () => harness.container.textContent!.includes('orders:read'),
      'thông báo không có quyền',
    );

    expect(orderRequests()).toHaveLength(0);
  });
});
