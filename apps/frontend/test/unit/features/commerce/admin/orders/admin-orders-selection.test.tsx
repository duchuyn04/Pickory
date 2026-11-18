// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, Profiler } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminOrdersPage } from '../../../../../../src/features/commerce/admin/orders/admin-orders-page';
import {
  actorWith,
  clickButton,
  createHarness,
  findButton,
  findField,
  selectOption,
  waitFor,
  type AdminListHarness,
} from '../../../identity/admin/admin-list-test-kit';

let harness: AdminListHarness;
let hasHarness = false;

afterEach(async () => {
  if (hasHarness) {
    await harness.cleanup();
    hasHarness = false;
  }
  vi.restoreAllMocks();
});

function orderRow(code: string) {
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
  };
}

function ordersResponse(codes: string[], total = codes.length, page = 1, limit = 20) {
  return {
    items: codes.map(orderRow),
    total,
    page,
    limit,
  };
}

function makeCodes(count: number, prefix = 'PK'): string[] {
  return Array.from(
    { length: count },
    (_, index) => `${prefix}-${String(index + 1).padStart(2, '0')}`,
  );
}

function open(url: string, respond: (requested: string) => unknown, permissions = ['orders:read']) {
  harness = createHarness(actorWith(permissions), respond);
  hasHarness = true;
  return harness.render(url);
}

function rowCheckbox(code: string): HTMLInputElement {
  const input = harness.container.querySelector<HTMLInputElement>(
    `input[type="checkbox"][aria-label="Chọn đơn ${code}"]`,
  );
  if (!input) {
    throw new Error(`Không thấy ô chọn của đơn ${code}.`);
  }
  return input;
}

function selectAllCheckbox(): HTMLInputElement {
  const input = harness.container.querySelector<HTMLInputElement>(
    'input[type="checkbox"][aria-label="Chọn tất cả đơn trong trang"]',
  );
  if (!input) {
    throw new Error('Không thấy ô chọn tất cả trong trang.');
  }
  return input;
}

function selectionCountText(): string {
  const element = harness.container.querySelector('.order-selection-count');
  return element?.textContent?.trim() ?? '';
}

function printButton(): HTMLButtonElement {
  const button = findButton(harness.container, 'In hóa đơn');
  if (!button) {
    throw new Error('Không thấy nút In hóa đơn.');
  }
  return button;
}

async function clickCheckbox(checkbox: HTMLInputElement) {
  await act(async () => {
    checkbox.click();
  });
}

async function setTextInput(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
  await act(async () => {
    setter.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
}

describe('chọn nhiều đơn để in ở danh sách đơn hàng', () => {
  it('mỗi dòng có ô chọn tên Chọn đơn <mã> và tiêu đề có ô chọn tất cả', async () => {
    await open('/admin/orders', () => ordersResponse(['PK-1', 'PK-2', 'PK-3']));
    await waitFor(
      () => harness.container.querySelectorAll('tbody tr').length === 3,
      'danh sách 3 đơn',
    );

    expect(selectAllCheckbox().getAttribute('aria-label')).toBe('Chọn tất cả đơn trong trang');
    for (const code of ['PK-1', 'PK-2', 'PK-3']) {
      expect(rowCheckbox(code).getAttribute('aria-label')).toBe(`Chọn đơn ${code}`);
    }
    expect(selectionCountText()).toBe('Đã chọn 0 đơn');
    const countNode = harness.container.querySelector('.order-selection-count')!;
    expect(countNode.getAttribute('aria-live')).toBe('polite');
    expect(countNode.getAttribute('aria-atomic')).toBe('true');
    expect(printButton().disabled).toBe(true);
  });

  it('cập nhật Đã chọn N đơn, mở khóa nút In và giữ đúng thứ tự dòng trong URL', async () => {
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);
    await open('/admin/orders', () => ordersResponse(['PK-1', 'PK-2', 'PK-3', 'PK-4']));
    await waitFor(
      () => harness.container.querySelectorAll('tbody tr').length === 4,
      'danh sách 4 đơn',
    );

    expect(printButton().disabled).toBe(true);

    // Bấm chọn theo thứ tự đảo (3, 1, 2) để kiểm tra URL vẫn sắp xếp theo thứ tự bảng.
    await clickCheckbox(rowCheckbox('PK-3'));
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');
    expect(printButton().disabled).toBe(false);

    await clickCheckbox(rowCheckbox('PK-1'));
    await clickCheckbox(rowCheckbox('PK-2'));
    expect(selectionCountText()).toBe('Đã chọn 3 đơn');

    await clickButton(harness.container, 'In hóa đơn');
    expect(openSpy).toHaveBeenCalledTimes(1);
    expect(openSpy).toHaveBeenCalledWith(
      '/admin/orders/print?codes=PK-1,PK-2,PK-3',
      '_blank',
      'noopener,noreferrer',
    );
  });

  it('ô chọn tất cả có đủ ba trạng thái rỗng, lưng chừng và đủ cùng hành vi bấm đúng', async () => {
    await open('/admin/orders', () => ordersResponse(['PK-1', 'PK-2', 'PK-3']));
    await waitFor(
      () => harness.container.querySelectorAll('tbody tr').length === 3,
      'danh sách 3 đơn',
    );

    const header = selectAllCheckbox();
    expect(header.checked).toBe(false);
    expect(header.indeterminate).toBe(false);
    expect(header.getAttribute('aria-checked')).toBeNull();

    // Chọn 1 trên 3 dòng tạo trạng thái lưng chừng.
    await clickCheckbox(rowCheckbox('PK-2'));
    expect(header.checked).toBe(false);
    expect(header.indeterminate).toBe(true);
    expect(header.getAttribute('aria-checked')).toBe('mixed');

    // Bấm từ lưng chừng chọn hết mọi dòng trong trang.
    await clickCheckbox(header);
    expect(selectionCountText()).toBe('Đã chọn 3 đơn');
    expect(header.checked).toBe(true);
    expect(header.indeterminate).toBe(false);
    expect(header.getAttribute('aria-checked')).toBeNull();

    // Bấm khi đang tích đủ thì bỏ chọn hết.
    await clickCheckbox(header);
    expect(selectionCountText()).toBe('Đã chọn 0 đơn');
    expect(header.checked).toBe(false);
    expect(header.indeterminate).toBe(false);

    // Bấm từ rỗng thì chọn hết mọi dòng trong trang.
    await clickCheckbox(header);
    expect(selectionCountText()).toBe('Đã chọn 3 đơn');
    expect(header.checked).toBe(true);
  });

  it('giới hạn 50 đơn: khóa ô chưa chọn, hiện chú thích và mở lại khi bỏ chọn bớt', async () => {
    const codes = makeCodes(60);
    await open('/admin/orders', () => ordersResponse(codes, 60, 1, 60));
    await waitFor(
      () => harness.container.querySelectorAll('tbody tr').length === 60,
      'danh sách 60 đơn',
    );

    // Bấm chọn tất cả trên trang 60 dòng chỉ chọn đúng 50 dòng đầu.
    await clickCheckbox(selectAllCheckbox());
    expect(selectionCountText()).toBe('Đã chọn 50 đơn');

    const limitNote = harness.container.querySelector('.order-selection-limit');
    expect(limitNote).not.toBeNull();
    expect(limitNote!.textContent).toBe('Mỗi lần in tối đa 50 đơn');
    expect(limitNote!.getAttribute('aria-live')).toBe('polite');
    expect(limitNote!.id).not.toBe('');

    // Các ô đã chọn vẫn mở để bỏ chọn được, còn các ô chưa chọn bị khóa và nối chú thích.
    expect(rowCheckbox(codes[0]!).disabled).toBe(false);
    expect(rowCheckbox(codes[49]!).disabled).toBe(false);
    expect(rowCheckbox(codes[50]!).disabled).toBe(true);
    expect(rowCheckbox(codes[50]!).getAttribute('aria-describedby')).toBe(limitNote!.id);
    expect(rowCheckbox(codes[59]!).disabled).toBe(true);

    // Bỏ chọn 1 đơn làm số lượng xuống 49, mở khóa ô khác và ẩn chú thích.
    await clickCheckbox(rowCheckbox(codes[0]!));
    expect(selectionCountText()).toBe('Đã chọn 49 đơn');
    expect(selectAllCheckbox().indeterminate).toBe(true);
    expect(rowCheckbox(codes[50]!).disabled).toBe(false);
    expect(rowCheckbox(codes[50]!).getAttribute('aria-describedby')).toBeNull();
    expect(harness.container.querySelector('.order-selection-limit')).toBeNull();

    // Chọn lại một ô khác để chạm ngưỡng 50 thì các ô còn lại bị khóa trở lại.
    await clickCheckbox(rowCheckbox(codes[50]!));
    expect(selectionCountText()).toBe('Đã chọn 50 đơn');
    expect(rowCheckbox(codes[0]!).disabled).toBe(true);
    expect(rowCheckbox(codes[51]!).disabled).toBe(true);
    expect(harness.container.querySelector('.order-selection-limit')).not.toBeNull();
  });

  it('đổi trang thì bỏ chọn hết và không in nhầm mã của trang cũ', async () => {
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);
    await open('/admin/orders', (url) => {
      if (url.includes('page=2')) {
        return ordersResponse(['PK-01', 'PK-21', 'PK-22'], 40, 2, 20);
      }
      return ordersResponse(['PK-01', 'PK-02'], 40, 1, 20);
    });
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-01"]') !== null,
      'trang 1',
    );

    await clickCheckbox(rowCheckbox('PK-01'));
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');

    await clickButton(harness.container, 'Trang sau');
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-21"]') !== null,
      'trang 2',
    );
    expect(selectionCountText()).toBe('Đã chọn 0 đơn');
    expect(printButton().disabled).toBe(true);

    // Chọn đơn mới ở trang 2 rồi in để chứng minh không còn mã trang 1.
    await clickCheckbox(rowCheckbox('PK-22'));
    await clickButton(harness.container, 'In hóa đơn');
    expect(openSpy).toHaveBeenCalledWith(
      '/admin/orders/print?codes=PK-22',
      '_blank',
      'noopener,noreferrer',
    );
  });

  it('đổi tab trạng thái thì bỏ chọn hết', async () => {
    await open('/admin/orders', (url) =>
      url.includes('status=delivered')
        ? ordersResponse(['PK-A1', 'PK-D1'])
        : ordersResponse(['PK-A1', 'PK-A2']),
    );
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-A1"]') !== null,
      'danh sách ban đầu',
    );

    await clickCheckbox(rowCheckbox('PK-A1'));
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');

    await clickButton(harness.container, 'Đã giao hàng');
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-D1"]') !== null,
      'tab Đã giao hàng',
    );
    expect(selectionCountText()).toBe('Đã chọn 0 đơn');
  });

  it('đổi bộ lọc từ khóa thì bỏ chọn hết', async () => {
    await open('/admin/orders', (url) =>
      url.includes('search=PK-B') ? ordersResponse(['PK-A1', 'PK-B1']) : ordersResponse(['PK-A1']),
    );
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-A1"]') !== null,
      'danh sách ban đầu',
    );

    await clickCheckbox(rowCheckbox('PK-A1'));
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');

    const searchInput = findField(
      harness.container,
      'Tìm theo mã đơn, tên, số điện thoại hoặc email',
    )!;
    await setTextInput(searchInput, 'PK-B');
    await act(async () => {
      searchInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    });
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-B1"]') !== null,
      'kết quả tìm kiếm',
    );
    expect(selectionCountText()).toBe('Đã chọn 0 đơn');
  });

  it('đổi bộ lọc thanh toán thì bỏ chọn hết', async () => {
    await open('/admin/orders', (url) =>
      url.includes('paymentStatus=unpaid')
        ? ordersResponse(['PK-A1', 'PK-U1'])
        : ordersResponse(['PK-A1']),
    );
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-A1"]') !== null,
      'danh sách ban đầu',
    );

    await clickCheckbox(rowCheckbox('PK-A1'));
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');

    await selectOption(findField(harness.container, 'Thanh toán')!, 'unpaid');
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-U1"]') !== null,
      'lọc thanh toán',
    );
    expect(selectionCountText()).toBe('Đã chọn 0 đơn');
  });

  it('đổi bộ lọc phương thức thì bỏ chọn hết', async () => {
    await open('/admin/orders', (url) =>
      url.includes('paymentMethod=cod')
        ? ordersResponse(['PK-A1', 'PK-C1'])
        : ordersResponse(['PK-A1']),
    );
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-A1"]') !== null,
      'danh sách ban đầu',
    );

    await clickCheckbox(rowCheckbox('PK-A1'));
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');

    await selectOption(findField(harness.container, 'Phương thức')!, 'cod');
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-C1"]') !== null,
      'lọc phương thức',
    );
    expect(selectionCountText()).toBe('Đã chọn 0 đơn');
  });

  it('đổi bộ lọc ghi chú thì bỏ chọn hết', async () => {
    await open('/admin/orders', (url) =>
      url.includes('flag=underpaid')
        ? ordersResponse(['PK-A1', 'PK-F1'])
        : ordersResponse(['PK-A1']),
    );
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-A1"]') !== null,
      'danh sách ban đầu',
    );

    await clickCheckbox(rowCheckbox('PK-A1'));
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');

    await selectOption(findField(harness.container, 'Ghi chú')!, 'underpaid');
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-F1"]') !== null,
      'lọc ghi chú',
    );
    expect(selectionCountText()).toBe('Đã chọn 0 đơn');
  });

  it('đổi bộ lọc từ ngày thì bỏ chọn hết', async () => {
    await open('/admin/orders', (url) =>
      url.includes('from=2026-10-01')
        ? ordersResponse(['PK-A1', 'PK-FR1'])
        : ordersResponse(['PK-A1']),
    );
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-A1"]') !== null,
      'danh sách ban đầu',
    );

    await clickCheckbox(rowCheckbox('PK-A1'));
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');

    await setTextInput(findField(harness.container, 'Từ ngày')!, '2026-10-01');
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-FR1"]') !== null,
      'lọc từ ngày',
    );
    expect(selectionCountText()).toBe('Đã chọn 0 đơn');
  });

  it('đổi bộ lọc đến ngày thì bỏ chọn hết', async () => {
    await open('/admin/orders', (url) =>
      url.includes('to=2026-10-12')
        ? ordersResponse(['PK-A1', 'PK-TO1'])
        : ordersResponse(['PK-A1']),
    );
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-A1"]') !== null,
      'danh sách ban đầu',
    );

    await clickCheckbox(rowCheckbox('PK-A1'));
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');

    await setTextInput(findField(harness.container, 'Đến ngày')!, '2026-10-12');
    await waitFor(
      () => harness.container.querySelector('a[href="/admin/orders/PK-TO1"]') !== null,
      'lọc đến ngày',
    );
    expect(selectionCountText()).toBe('Đã chọn 0 đơn');
  });

  it('nạp lại cùng trang mà đơn đã chọn biến mất thì loại đơn đó khỏi lựa chọn', async () => {
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);
    let callCount = 0;
    await open('/admin/orders', () => {
      callCount += 1;
      if (callCount === 1) {
        return ordersResponse(['PK-1', 'PK-2', 'PK-3']);
      }
      return ordersResponse(['PK-1', 'PK-3']);
    });
    await waitFor(() => harness.container.querySelectorAll('tbody tr').length === 3, 'lần nạp đầu');

    await clickCheckbox(rowCheckbox('PK-1'));
    await clickCheckbox(rowCheckbox('PK-2'));
    expect(selectionCountText()).toBe('Đã chọn 2 đơn');

    // Ép truy vấn nạp lại cùng bộ lọc và trang khi đơn PK-2 không còn trong danh sách.
    await act(async () => {
      await harness.client.invalidateQueries({ queryKey: ['admin-orders'] });
    });
    await waitFor(() => harness.container.querySelectorAll('tbody tr').length === 2, 'lần nạp lại');

    expect(selectionCountText()).toBe('Đã chọn 1 đơn');
    await clickButton(harness.container, 'In hóa đơn');
    expect(openSpy).toHaveBeenCalledWith(
      '/admin/orders/print?codes=PK-1',
      '_blank',
      'noopener,noreferrer',
    );
  });

  it('đơn biến mất rồi xuất hiện lại khi nạp lại cùng trang không tự được chọn lại', async () => {
    let phase = 1;
    await open('/admin/orders', () => {
      if (phase === 2) {
        return ordersResponse(['PK-1', 'PK-3']);
      }
      return ordersResponse(['PK-1', 'PK-2', 'PK-3']);
    });
    await waitFor(() => harness.container.querySelectorAll('tbody tr').length === 3, 'lần nạp 1');

    await clickCheckbox(rowCheckbox('PK-1'));
    await clickCheckbox(rowCheckbox('PK-2'));
    expect(selectionCountText()).toBe('Đã chọn 2 đơn');

    // Lần nạp 2 làm PK-2 biến mất nên bị cắt khỏi lựa chọn.
    phase = 2;
    await act(async () => {
      await harness.client.invalidateQueries({ queryKey: ['admin-orders'] });
    });
    await waitFor(() => harness.container.querySelectorAll('tbody tr').length === 2, 'lần nạp 2');
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');

    // Lần nạp 3 có PK-2 quay lại nhưng không được tự tích lại.
    phase = 3;
    await act(async () => {
      await harness.client.invalidateQueries({ queryKey: ['admin-orders'] });
    });
    await waitFor(() => harness.container.querySelectorAll('tbody tr').length === 3, 'lần nạp 3');
    expect(selectionCountText()).toBe('Đã chọn 1 đơn');
    expect(rowCheckbox('PK-2').checked).toBe(false);
  });

  it('giữ trạng thái lưng chừng ở ô chọn tất cả khi bảng gắn lại sau lỗi tạm thời', async () => {
    let shouldFail = false;
    await open('/admin/orders', () => {
      if (shouldFail) {
        return new Response(
          JSON.stringify({ error: { code: 'SERVER_ERROR', message: 'Lỗi tạm thời.' } }),
          { status: 500 },
        );
      }
      return ordersResponse(['PK-1', 'PK-2', 'PK-3']);
    });
    await waitFor(() => harness.container.querySelectorAll('tbody tr').length === 3, 'lần nạp đầu');

    await clickCheckbox(rowCheckbox('PK-1'));
    expect(selectAllCheckbox().indeterminate).toBe(true);

    // Lần nạp lại gặp lỗi khiến bảng bị thay bằng hộp thông báo lỗi.
    shouldFail = true;
    await act(async () => {
      await harness.client.invalidateQueries({ queryKey: ['admin-orders'] });
    });
    await waitFor(() => harness.container.querySelector('[role="alert"]') !== null, 'lỗi nạp lại');

    // Khi khôi phục thành công và bảng gắn lại, ô tiêu đề vẫn giữ trạng thái lưng chừng.
    shouldFail = false;
    await act(async () => {
      await harness.client.invalidateQueries({ queryKey: ['admin-orders'] });
    });
    await waitFor(
      () => harness.container.querySelectorAll('tbody tr').length === 3,
      'bảng gắn lại',
    );
    expect(selectAllCheckbox().indeterminate).toBe(true);
    expect(selectAllCheckbox().getAttribute('aria-checked')).toBe('mixed');
  });

  it('xóa lựa chọn ngay trong lúc vẽ lại khi chuyển sang trang đã có sẵn trong cache', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        if (url === '/api/me') {
          return new Response(JSON.stringify({ data: actorWith(['orders:read']) }));
        }
        const data = url.includes('page=2')
          ? ordersResponse(['PK-01', 'PK-21'], 40, 2, 20)
          : ordersResponse(['PK-01', 'PK-02'], 40, 1, 20);
        return new Response(JSON.stringify({ data }));
      }),
    );

    const committedSnapshots: {
      hasPage1Row: boolean;
      countText: string;
      pk01Checked: boolean;
    }[] = [];
    let recordSnapshots = false;

    try {
      await act(async () => {
        root.render(
          <QueryClientProvider client={client}>
            <MemoryRouter initialEntries={['/admin/orders']}>
              <Routes>
                <Route
                  path="/admin/orders"
                  element={
                    <Profiler
                      id="orders-selection-reset"
                      onRender={() => {
                        if (!recordSnapshots) {
                          return;
                        }
                        committedSnapshots.push({
                          hasPage1Row:
                            container.querySelector('a[href="/admin/orders/PK-02"]') !== null,
                          countText:
                            container
                              .querySelector('.order-selection-count')
                              ?.textContent?.trim() ?? '',
                          pk01Checked:
                            container.querySelector<HTMLInputElement>(
                              'input[type="checkbox"][aria-label="Chọn đơn PK-01"]',
                            )?.checked ?? false,
                        });
                      }}
                    >
                      <AdminOrdersPage />
                    </Profiler>
                  }
                />
              </Routes>
            </MemoryRouter>
          </QueryClientProvider>,
        );
      });

      await waitFor(
        () => container.querySelector('a[href="/admin/orders/PK-02"]') !== null,
        'trang 1 sẵn sàng',
      );
      await clickButton(container, 'Trang sau');
      await waitFor(
        () => container.querySelector('a[href="/admin/orders/PK-21"]') !== null,
        'trang 2 sẵn sàng',
      );

      // Chọn đơn trùng mã PK-01 ở trang 2 rồi quay lại trang 1 đã có trong cache.
      await clickCheckbox(
        container.querySelector<HTMLInputElement>(
          'input[type="checkbox"][aria-label="Chọn đơn PK-01"]',
        )!,
      );
      expect(container.querySelector('.order-selection-count')?.textContent?.trim()).toBe(
        'Đã chọn 1 đơn',
      );

      recordSnapshots = true;
      await clickButton(container, 'Trang trước');

      // Khung hình đầu tiên vẽ trang 1 từ cache phải có lựa chọn rỗng ngay, không đợi effect.
      const page1Commits = committedSnapshots.filter((snapshot) => snapshot.hasPage1Row);
      expect(page1Commits.length).toBeGreaterThan(0);
      for (const snapshot of page1Commits) {
        expect(snapshot.countText).toBe('Đã chọn 0 đơn');
        expect(snapshot.pk01Checked).toBe(false);
      }
    } finally {
      await act(async () => root.unmount());
      container.remove();
      client.clear();
      vi.unstubAllGlobals();
    }
  });

  it('không hiện thanh chọn khi đang nạp, khi lỗi hoặc khi danh sách rỗng', async () => {
    // Đang nạp.
    await open('/admin/orders', () => new Promise(() => {}));
    await waitFor(
      () => harness.container.textContent?.includes('Đang nạp danh sách đơn hàng...') ?? false,
      'đang nạp',
    );
    expect(harness.container.querySelector('.order-selection-bar')).toBeNull();
    await harness.cleanup();

    // Lỗi API.
    await open(
      '/admin/orders',
      () =>
        new Response(JSON.stringify({ error: { code: 'SERVER_ERROR', message: 'Lỗi máy chủ.' } }), {
          status: 500,
        }),
    );
    await waitFor(
      () => harness.container.querySelector('[role="alert"]') !== null,
      'thông báo lỗi',
    );
    expect(harness.container.querySelector('.order-selection-bar')).toBeNull();
    await harness.cleanup();

    // Danh sách rỗng.
    await open('/admin/orders', () => ordersResponse([]));
    await waitFor(
      () => harness.container.textContent?.includes('Chưa có đơn hàng nào.') ?? false,
      'danh sách rỗng',
    );
    expect(harness.container.querySelector('.order-selection-bar')).toBeNull();
  });
});
