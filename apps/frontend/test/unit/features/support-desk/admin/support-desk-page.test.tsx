// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { clickButton, waitFor } from '../../identity/admin/admin-list-test-kit';
import {
  FakeEventSource,
  READ_AND_REPLY,
  READ_ONLY,
  activeEventSource,
  activeEventSources,
  callbackRequest,
  findTab,
  openDesk,
  openDeskPage,
  queueSession,
  supportRequests,
  textOf,
  type OpenedDesk,
} from './support-desk-test-kit';

let desk: OpenedDesk;

afterEach(async () => {
  await desk.harness.cleanup();
});

function waitingBadge(): string | null {
  const tab = findTab(desk.harness.container, 'Đang chờ');
  return tab?.querySelector('.tab-list-count')?.textContent ?? null;
}

async function emitFromServer(name: string, data: unknown) {
  await act(async () => activeEventSource().emit(name, data));
}

describe('màn CSKH: quyền truy cập', () => {
  it('người thiếu conversations:read thấy trang không đủ quyền và không gọi API support desk', async () => {
    desk = openDesk(['orders:read']);

    await desk.harness.render('/admin/conversations');
    await waitFor(() => textOf(desk.harness.container).includes('403'), 'trang không đủ quyền');

    expect(textOf(desk.harness.container)).toContain('conversations:read');
    expect(supportRequests(desk.harness)).toEqual([]);
    expect(FakeEventSource.instances).toHaveLength(0);
  });

  it('người có conversations:read thấy 3 cột: hàng chờ, khung chat, thông tin khách', async () => {
    desk = openDesk(READ_ONLY);

    await openDeskPage(desk);

    const headings = [...desk.harness.container.querySelectorAll('h2')].map(
      (heading) => heading.textContent,
    );
    expect(headings).toEqual(expect.arrayContaining(['Khung chat', 'Thông tin khách']));
    expect(desk.harness.container.querySelector('[aria-label="Hàng chờ"]')).not.toBeNull();
  });

  it('dùng một kết nối SSE cho cả trang lẫn công tắc, và đóng kết nối khi rời khỏi khu quản trị', async () => {
    desk = openDesk(READ_AND_REPLY);

    await openDeskPage(desk);

    expect(activeEventSources()).toHaveLength(1);
    const source = activeEventSource();

    await desk.harness.cleanup();
    expect(source.closeCalls).toBeGreaterThan(0);
    // `afterEach` gọi cleanup lần nữa. Thay bằng hàm rỗng để không gỡ gốc React hai lần.
    desk = { ...desk, harness: { ...desk.harness, cleanup: async () => {} } };
  });
});

describe('hàng chờ', () => {
  it('có ba tab Đang chờ, Của tôi, Gọi lại trong một tablist', async () => {
    desk = openDesk(READ_ONLY);

    await openDeskPage(desk);

    const tablist = desk.harness.container.querySelector('[role="tablist"]');
    const labels = [...(tablist?.querySelectorAll('[role="tab"]') ?? [])].map(
      (tab) => tab.textContent,
    );
    expect(labels).toEqual(['Đang chờ', 'Của tôi', 'Gọi lại']);
  });

  it('hiện phiên mới trong tab Đang chờ khi nhận session.waiting, không tải lại trang', async () => {
    desk = openDesk(READ_ONLY, { waiting: [queueSession('1')] });
    await openDeskPage(desk);
    await waitFor(() => textOf(desk.harness.container).includes('Khách hỏi về đơn 1'), 'phiên 1');
    expect(textOf(desk.harness.container)).not.toContain('Khách hỏi về đơn 2');

    desk.state.waiting = [queueSession('1'), queueSession('2')];
    await emitFromServer('session.waiting', { sessionId: '2' });

    await waitFor(() => textOf(desk.harness.container).includes('Khách hỏi về đơn 2'), 'phiên 2');
  });

  it('đọc lại danh sách khi nhận session.assigned và session.closed', async () => {
    desk = openDesk(READ_ONLY, { waiting: [queueSession('1'), queueSession('2')] });
    await openDeskPage(desk);
    await waitFor(() => waitingBadge() === '2', 'badge 2');

    desk.state.waiting = [queueSession('2')];
    await emitFromServer('session.assigned', { sessionId: '1' });
    await waitFor(() => waitingBadge() === '1', 'badge 1 sau khi nhận');

    desk.state.waiting = [];
    await emitFromServer('session.closed', { sessionId: '2' });
    await waitFor(() => waitingBadge() === null, 'hết badge sau khi đóng');
  });

  it('badge của tab Đang chờ khớp số phiên đang chờ', async () => {
    desk = openDesk(READ_ONLY, {
      waiting: [queueSession('1'), queueSession('2'), queueSession('3')],
    });

    await openDeskPage(desk);

    await waitFor(() => waitingBadge() === '3', 'badge 3');
    const rows = desk.harness.container.querySelectorAll('[aria-label="Phiên đang chờ"] li');
    expect(rows).toHaveLength(3);
  });

  it('không hiện badge khi hàng chờ trống', async () => {
    desk = openDesk(READ_ONLY, { waiting: [] });

    await openDeskPage(desk);
    await waitFor(
      () => textOf(desk.harness.container).includes('Chưa có khách nào đang chờ'),
      'trống',
    );

    expect(waitingBadge()).toBeNull();
  });

  it('tab Gọi lại chỉ hiện yêu cầu pending', async () => {
    desk = openDesk(READ_ONLY, {
      callbacks: [
        callbackRequest('1', 'pending'),
        callbackRequest('2', 'done'),
        callbackRequest('3', 'pending'),
      ],
    });
    await openDeskPage(desk);

    await clickButton(desk.harness.container, 'Gọi lại');

    await waitFor(() => textOf(desk.harness.container).includes('0900000001'), 'yêu cầu 1');
    const text = textOf(desk.harness.container);
    expect(text).toContain('0900000003');
    expect(text).not.toContain('0900000002');
  });

  it('chỉ đọc danh sách của tab đang mở, và đọc "Của tôi" khi chuyển tab', async () => {
    desk = openDesk(READ_ONLY);
    await openDeskPage(desk);
    await waitFor(
      () => supportRequests(desk.harness).some((url) => url.includes('view=waiting')),
      'đọc hàng chờ',
    );
    expect(supportRequests(desk.harness).some((url) => url.includes('view=mine'))).toBe(false);

    await clickButton(desk.harness.container, 'Của tôi');

    await waitFor(
      () => supportRequests(desk.harness).some((url) => url.includes('view=mine')),
      'đọc phiên của tôi',
    );
  });

  it('route chưa có trả 404: hiện lỗi ngắn và nút Thử lại, trang không vỡ, không có badge', async () => {
    desk = openDesk(READ_ONLY, { waitingMissing: true });

    await openDeskPage(desk);
    await waitFor(() => desk.harness.container.querySelector('[role="alert"]') !== null, 'lỗi');

    expect(textOf(desk.harness.container)).toContain('Không tải được hàng chờ');
    expect(waitingBadge()).toBeNull();
    expect(desk.harness.container.querySelector('[role="tablist"]')).not.toBeNull();

    desk.state.waitingMissing = false;
    desk.state.waiting = [queueSession('7')];
    await clickButton(desk.harness.container, 'Thử lại');

    await waitFor(
      () => textOf(desk.harness.container).includes('Khách hỏi về đơn 7'),
      'sau khi thử lại',
    );
    expect(desk.harness.container.querySelector('[role="alert"]')).toBeNull();
  });

  it('đọc lại danh sách mỗi lần luồng SSE mở lại, vì sự kiện lúc nối lại đã mất', async () => {
    desk = openDesk(READ_ONLY, { waiting: [queueSession('1')] });
    await openDeskPage(desk);
    await waitFor(() => waitingBadge() === '1', 'badge 1');

    desk.state.waiting = [queueSession('1'), queueSession('2')];
    await act(async () => activeEventSource().open());

    await waitFor(() => waitingBadge() === '2', 'badge 2 sau khi luồng mở lại');
  });
});

describe('bàn phím', () => {
  it('phím mũi tên đổi focus giữa các tab của hàng chờ', async () => {
    desk = openDesk(READ_ONLY);
    await openDeskPage(desk);
    const waitingTab = findTab(desk.harness.container, 'Đang chờ');
    const mineTab = findTab(desk.harness.container, 'Của tôi');
    waitingTab?.focus();

    await act(async () => {
      waitingTab?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    });

    expect(document.activeElement).toBe(mineTab);
  });
});

describe('thao tác phiên: chuyển, kết thúc và thông tin khách', () => {
  it('phiên đang giữ hiện nút Chuyển và Kết thúc, bấm Chuyển mở hộp thoại', async () => {
    const session = queueSession('s1', 'Khách cần hỗ trợ');
    desk = openDesk(READ_AND_REPLY, {
      mine: [session],
      availableStaffIds: ['staff-0002'],
    });

    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');

    // Mở phiên từ tab Của tôi (phiên đang giữ).
    await waitFor(
      () => textOf(desk.harness.container).includes('Khách cần hỗ trợ'),
      'phiên của tôi',
    );
    const sessionBtn = Array.from(
      desk.harness.container.querySelectorAll('.support-queue-item button, .support-queue-item'),
    ).find((el) => el.textContent?.includes('Khách cần hỗ trợ')) as HTMLElement;
    await act(async () => sessionBtn.click());

    // Hai nút Chuyển và Kết thúc xuất hiện trên khung chat.
    await waitFor(
      () => desk.harness.container.querySelector('.support-chat-actions') !== null,
      'nút thao tác phiên',
    );
    expect(textOf(desk.harness.container)).toContain('Chuyển');
    expect(textOf(desk.harness.container)).toContain('Kết thúc');

    // Bấm Chuyển mở hộp thoại chuyển phiên.
    await clickButton(desk.harness.container, 'Chuyển');
    await waitFor(
      () => desk.harness.container.querySelector('#transfer-session-title') !== null,
      'hộp thoại chuyển phiên',
    );
    expect(textOf(desk.harness.container)).toContain('Chuyển hội thoại');
  });

  it('bấm Kết thúc mở hộp thoại xác nhận trước khi gọi API', async () => {
    const session = queueSession('s1', 'Khách cần hỗ trợ');
    desk = openDesk(READ_AND_REPLY, {
      mine: [session],
    });

    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');

    await waitFor(
      () => textOf(desk.harness.container).includes('Khách cần hỗ trợ'),
      'phiên của tôi',
    );
    const sessionBtn = Array.from(
      desk.harness.container.querySelectorAll('.support-queue-item button, .support-queue-item'),
    ).find((el) => el.textContent?.includes('Khách cần hỗ trợ')) as HTMLElement;
    await act(async () => sessionBtn.click());

    await waitFor(
      () => desk.harness.container.querySelector('.support-chat-actions') !== null,
      'nút thao tác phiên',
    );

    const closeRequests = () =>
      desk.harness.writes.filter((w) => w.method === 'PATCH' && w.url.includes('/sessions/s1'));

    // Trước khi bấm nút, chưa có request đóng phiên nào được gửi.
    expect(closeRequests()).toHaveLength(0);

    // Bấm Kết thúc ở thanh trên khung chat.
    await clickButton(desk.harness.container, 'Kết thúc');

    // Hộp thoại xác nhận xuất hiện và chưa gọi API đóng phiên.
    await waitFor(
      () => desk.harness.container.querySelector('#end-session-title') !== null,
      'hộp thoại kết thúc phiên',
    );
    expect(textOf(desk.harness.container)).toContain('Kết thúc hội thoại này?');
    expect(closeRequests()).toHaveLength(0);

    // Bấm nút Hủy trong hộp thoại: hộp thoại đóng lại và vẫn không gọi API đóng phiên.
    await clickButton(desk.harness.container, 'Hủy');
    await waitFor(
      () => desk.harness.container.querySelector('#end-session-title') === null,
      'hộp thoại đóng lại sau khi hủy',
    );
    expect(closeRequests()).toHaveLength(0);

    // Bấm lại Kết thúc ở thanh trên và xác nhận đóng phiên trong hộp thoại.
    await clickButton(desk.harness.container, 'Kết thúc');
    await waitFor(
      () => desk.harness.container.querySelector('#end-session-title') !== null,
      'hộp thoại mở lại',
    );
    const dialogConfirmBtn = desk.harness.container.querySelector(
      '.dialog-foot button.btn-danger',
    ) as HTMLButtonElement;
    await act(async () => dialogConfirmBtn.click());

    // Chỉ gọi API PATCH kết thúc phiên đúng 1 lần sau khi người dùng xác nhận.
    await waitFor(() => closeRequests().length === 1, 'gọi API đóng phiên');
    expect(closeRequests()).toHaveLength(1);
  });

  it('chuyển thành công hiện dòng status thông báo và đưa khung chat về chưa chọn phiên', async () => {
    const session = queueSession('s1', 'Khách cần hỗ trợ');
    desk = openDesk(READ_AND_REPLY, {
      mine: [session],
      availableStaffIds: ['staff-0002'],
    });

    await openDeskPage(desk);

    // Vùng status thông báo luôn có mặt trong DOM ngay từ đầu để trình đọc màn hình bắt được.
    const statusNotice = desk.harness.container.querySelector(
      '[role="status"].support-transfer-status',
    ) as HTMLElement;
    expect(statusNotice).not.toBeNull();
    expect(statusNotice.textContent).toBe('');

    await clickButton(desk.harness.container, 'Của tôi');

    await waitFor(
      () => textOf(desk.harness.container).includes('Khách cần hỗ trợ'),
      'phiên của tôi',
    );
    const sessionBtn = Array.from(
      desk.harness.container.querySelectorAll('.support-queue-item button, .support-queue-item'),
    ).find((el) => el.textContent?.includes('Khách cần hỗ trợ')) as HTMLElement;
    await act(async () => sessionBtn.click());

    await waitFor(
      () => desk.harness.container.querySelector('.support-chat-actions') !== null,
      'nút thao tác',
    );
    await clickButton(desk.harness.container, 'Chuyển');

    await waitFor(
      () => desk.harness.container.querySelector('input[type="radio"]') !== null,
      'radio nhân viên',
    );
    const radio = desk.harness.container.querySelector('input[type="radio"]') as HTMLInputElement;
    await act(async () => radio.click());

    // Bấm nút Chuyển trong hộp thoại.
    const dialogConfirmBtn = desk.harness.container.querySelector(
      '.dialog-foot button.btn-primary',
    ) as HTMLButtonElement;
    await act(async () => dialogConfirmBtn.click());

    // Thông báo role="status" cập nhật nội dung và nhận tiêu điểm.
    await waitFor(
      () => statusNotice.textContent?.includes('Đã chuyển hội thoại cho') ?? false,
      'thông báo chuyển hiển thị',
    );
    expect(statusNotice.textContent).toContain('Đã chuyển hội thoại cho');
    await waitFor(() => document.activeElement === statusNotice, 'focus chuyển về thông báo');
    expect(document.activeElement).toBe(statusNotice);

    // Khung chat quay về trạng thái chưa chọn phiên.
    expect(textOf(desk.harness.container)).toContain(
      'Tin nhắn của hội thoại đang mở sẽ hiện ở đây.',
    );
  });

  it('chèn tên khách và mã đơn thật vào mẫu trả lời nhanh khi phiên có dữ liệu', async () => {
    const session = queueSession('s1', 'Khách cần hỗ trợ');
    desk = openDesk(READ_AND_REPLY, {
      mine: [session],
      cannedResponses: [
        {
          id: 'ca1',
          shortcut: 'camon',
          body: 'Cảm ơn {ten_khach} đã liên hệ, đơn {ma_don} đang giao.',
          shared: true,
          editable: false,
        },
      ],
      customerInfo: {
        customer: { isGuest: false, name: 'Nguyễn Minh Anh', email: 'minhanh@example.com' },
        orders: [
          {
            code: 'PK123',
            status: 'shipping',
            total: 459000,
            itemCount: 2,
            createdAt: '2026-11-01T09:12:00Z',
          },
          {
            code: 'PK098',
            status: 'completed',
            total: 210000,
            itemCount: 1,
            createdAt: '2026-10-15T08:00:00Z',
          },
        ],
      },
    });

    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');

    await waitFor(
      () => textOf(desk.harness.container).includes('Khách cần hỗ trợ'),
      'phiên của tôi',
    );
    const sessionBtn = Array.from(
      desk.harness.container.querySelectorAll('.support-queue-item button, .support-queue-item'),
    ).find((el) => el.textContent?.includes('Khách cần hỗ trợ')) as HTMLElement;
    await act(async () => sessionBtn.click());

    // Chờ ô soạn xuất hiện và gõ phím tắt mẫu.
    await waitFor(() => desk.harness.container.querySelector('textarea') !== null, 'ô soạn');
    const textarea = desk.harness.container.querySelector('textarea') as HTMLTextAreaElement;
    const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;

    await act(async () => {
      setter.call(textarea, '/camon');
      textarea.dispatchEvent(new Event('input', { bubbles: true }));
      textarea.dispatchEvent(new Event('change', { bubbles: true }));
    });

    // Bấm phím Enter để chèn mẫu vào ô soạn.
    await act(async () => {
      textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    });

    // Ưu tiên đơn mới nhất đầu mảng (PK123) và không điền đơn cũ (PK098).
    expect(textarea.value).toBe('Cảm ơn Nguyễn Minh Anh đã liên hệ, đơn PK123 đang giao.');
    expect(textarea.value).not.toContain('PK098');
  });

  it('giữ nguyên placeholder khi thiếu đơn hàng, thiếu tên hoặc là khách vãng lai', async () => {
    const cases = [
      {
        name: 'khách không có đơn',
        customer: { isGuest: false, name: 'Nguyễn Minh Anh', email: 'minhanh@example.com' },
        orders: [],
        expected: 'Cảm ơn Nguyễn Minh Anh đã liên hệ, đơn {ma_don} đang giao.',
      },
      {
        name: 'tên bằng null',
        customer: { isGuest: false, name: null, email: 'minhanh@example.com' },
        orders: [
          {
            code: 'PK123',
            status: 'shipping',
            total: 100000,
            itemCount: 1,
            createdAt: '2026-11-01T09:00:00Z',
          },
        ],
        expected: 'Cảm ơn {ten_khach} đã liên hệ, đơn PK123 đang giao.',
      },
      {
        name: 'tên chỉ có khoảng trắng',
        customer: { isGuest: false, name: '   ', email: 'minhanh@example.com' },
        orders: [
          {
            code: 'PK123',
            status: 'shipping',
            total: 100000,
            itemCount: 1,
            createdAt: '2026-11-01T09:00:00Z',
          },
        ],
        expected: 'Cảm ơn {ten_khach} đã liên hệ, đơn PK123 đang giao.',
      },
      {
        name: 'khách vãng lai',
        customer: { isGuest: true, name: null, email: null },
        orders: [],
        expected: 'Cảm ơn {ten_khach} đã liên hệ, đơn {ma_don} đang giao.',
      },
    ];

    for (const testCase of cases) {
      if (desk) await desk.harness.cleanup();
      desk = openDesk(READ_AND_REPLY, {
        mine: [queueSession('s1', 'Khách cần hỗ trợ')],
        cannedResponses: [
          {
            id: 'ca1',
            shortcut: 'camon',
            body: 'Cảm ơn {ten_khach} đã liên hệ, đơn {ma_don} đang giao.',
            shared: true,
            editable: false,
          },
        ],
        customerInfo: {
          customer: testCase.customer,
          orders: testCase.orders,
        },
      });

      await openDeskPage(desk);
      await clickButton(desk.harness.container, 'Của tôi');
      await waitFor(
        () => textOf(desk.harness.container).includes('Khách cần hỗ trợ'),
        'phiên của tôi',
      );
      const sessionBtn = Array.from(
        desk.harness.container.querySelectorAll('.support-queue-item button, .support-queue-item'),
      ).find((el) => el.textContent?.includes('Khách cần hỗ trợ')) as HTMLElement;
      await act(async () => sessionBtn.click());

      await waitFor(() => desk.harness.container.querySelector('textarea') !== null, 'ô soạn');
      const textarea = desk.harness.container.querySelector('textarea') as HTMLTextAreaElement;
      const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;

      await act(async () => {
        setter.call(textarea, '/camon');
        textarea.dispatchEvent(new Event('input', { bubbles: true }));
        textarea.dispatchEvent(new Event('change', { bubbles: true }));
      });

      await act(async () => {
        textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      });

      expect(textarea.value).toBe(testCase.expected);
    }
  });

  it('giữ nguyên placeholder khi query thông tin khách chưa phản hồi xong', async () => {
    let resolveCustomer: (val: unknown) => void = () => {};
    const pendingCustomer = new Promise((resolve) => {
      resolveCustomer = resolve;
    });

    desk = openDesk(READ_AND_REPLY, {
      mine: [queueSession('s1', 'Khách cần hỗ trợ')],
      cannedResponses: [
        {
          id: 'ca1',
          shortcut: 'camon',
          body: 'Cảm ơn {ten_khach} đã liên hệ, đơn {ma_don} đang giao.',
          shared: true,
          editable: false,
        },
      ],
      customerInfo: pendingCustomer,
    });

    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Khách cần hỗ trợ'),
      'phiên của tôi',
    );
    const sessionBtn = Array.from(
      desk.harness.container.querySelectorAll('.support-queue-item button, .support-queue-item'),
    ).find((el) => el.textContent?.includes('Khách cần hỗ trợ')) as HTMLElement;
    await act(async () => sessionBtn.click());

    // Ô soạn xuất hiện trong lúc bảng thông tin khách hàng vẫn đang tải dữ liệu.
    await waitFor(() => desk.harness.container.querySelector('textarea') !== null, 'ô soạn');
    const textarea = desk.harness.container.querySelector('textarea') as HTMLTextAreaElement;
    const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;

    await act(async () => {
      setter.call(textarea, '/camon');
      textarea.dispatchEvent(new Event('input', { bubbles: true }));
      textarea.dispatchEvent(new Event('change', { bubbles: true }));
    });

    await act(async () => {
      textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    });

    // Khi dữ liệu khách chưa có, các biến mẫu giữ nguyên placeholder để không mất chữ.
    expect(textarea.value).toBe('Cảm ơn {ten_khach} đã liên hệ, đơn {ma_don} đang giao.');

    // Cho request nạp xong để dọn dẹp state sạch sẽ.
    await act(async () => {
      resolveCustomer({
        customer: { isGuest: false, name: 'Nguyễn Minh Anh', email: 'minhanh@example.com' },
        orders: [],
      });
    });
  });

  it('đổi sang phiên khác tự động đóng ngăn thông tin khách qua key', async () => {
    const session1 = queueSession('s1', 'Khách 1');
    const session2 = queueSession('s2', 'Khách 2');
    desk = openDesk(READ_AND_REPLY, {
      mine: [session1, session2],
    });

    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await waitFor(() => textOf(desk.harness.container).includes('Khách 1'), 'phiên 1');

    const sessionBtns = Array.from(
      desk.harness.container.querySelectorAll('.support-queue-item button, .support-queue-item'),
    ) as HTMLElement[];
    const btn1 = sessionBtns.find((el) => el.textContent?.includes('Khách 1'));
    const btn2 = sessionBtns.find((el) => el.textContent?.includes('Khách 2'));

    // Mở phiên 1.
    await act(async () => btn1?.click());

    // Nút mở ngăn thông tin khách có aria-expanded bằng false khi vừa mở phiên.
    const toggleBtn = desk.harness.container.querySelector(
      '.support-customer-toggle',
    ) as HTMLButtonElement;
    expect(toggleBtn).not.toBeNull();
    expect(toggleBtn.getAttribute('aria-expanded')).toBe('false');

    // Bấm mở ngăn thông tin khách.
    await act(async () => toggleBtn.click());
    expect(toggleBtn.getAttribute('aria-expanded')).toBe('true');

    // Chọn phiên 2: SupportDeskPage thay đổi key của CustomerPanel và dựng lại component.
    await act(async () => btn2?.click());

    // Sau khi đổi phiên, component mới được dựng lại nên ngăn tự động đóng lại.
    const newToggleBtn = desk.harness.container.querySelector(
      '.support-customer-toggle',
    ) as HTMLButtonElement;
    expect(newToggleBtn.getAttribute('aria-expanded')).toBe('false');
  });
});
