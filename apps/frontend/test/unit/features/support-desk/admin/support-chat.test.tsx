// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { clickButton, waitFor } from '../../identity/admin/admin-list-test-kit';
import {
  READ_AND_REPLY,
  READ_ONLY,
  activeEventSource,
  openDesk,
  openDeskPage,
  queueSession,
  supportRequests,
  textOf,
  type OpenedDesk,
} from './support-desk-test-kit';
import * as supportApi from '../../../../../src/features/support-desk/admin/support-desk-api';
import type { SessionAssignment } from '../../../../../src/features/support-desk/admin/support-desk-schemas';

let desk: OpenedDesk;

afterEach(async () => {
  await desk.harness.cleanup();
});

interface StaffMessageSeed {
  id: string;
  sessionId?: string | null;
  role?: string;
  body: string;
  action?: string | null;
  staffDisplayName?: string | null;
  createdAt?: string;
}

// Tin mẫu đúng hợp đồng `StaffViewMessage`. Test chỉ đổi phần liên quan tới case.
function staffMessage(seed: StaffMessageSeed) {
  return {
    sessionId: 'session-1',
    role: 'customer',
    action: null,
    staffDisplayName: null,
    createdAt: '2026-10-22T08:00:00Z',
    ...seed,
  };
}

const HISTORY_PAGE = {
  items: [
    staffMessage({ id: 'm1', body: 'Đơn của mình đâu?' }),
    staffMessage({
      id: 'm2',
      role: 'staff',
      body: 'Mình kiểm ngay.',
      staffDisplayName: 'Lan - CSKH',
    }),
    staffMessage({ id: 'm3', role: 'system', body: 'Phiên đã kết thúc.', action: 'csat_form' }),
  ],
  nextCursor: null,
};

// Dòng hàng chờ là một nút. Helper chờ dòng nạp xong rồi mới bấm, để test không phụ thuộc tốc độ nạp.
async function selectQueueRow(listLabel: string) {
  await waitFor(() => {
    const list = desk.harness.container.querySelector(`[aria-label="${listLabel}"]`);
    return list?.querySelector('button') !== null && list?.querySelector('button') !== undefined;
  }, `dòng trong danh sách ${listLabel}`);
  const list = desk.harness.container.querySelector(`[aria-label="${listLabel}"]`);
  const row = list?.querySelector('button');
  if (!row) throw new Error(`Thiếu dòng trong danh sách ${listLabel}.`);
  await act(async () => row.click());
}

function messageRequests(): string[] {
  return supportRequests(desk.harness).filter((url) => url.includes('/conversations/'));
}

function composer(): HTMLTextAreaElement | null {
  return desk.harness.container.querySelector('[role="combobox"]');
}

async function emitFromServer(name: string, data: unknown) {
  await act(async () => activeEventSource().emit(name, data));
}

describe('khung chat: chọn phiên và lịch sử tin', () => {
  it('chưa chọn phiên: giữ câu hướng dẫn và không đọc lịch sử', async () => {
    desk = openDesk(READ_AND_REPLY, { waiting: [queueSession('1')] });
    await openDeskPage(desk);

    expect(textOf(desk.harness.container)).toContain(
      'Tin nhắn của hội thoại đang mở sẽ hiện ở đây.',
    );
    expect(messageRequests()).toEqual([]);
  });

  it('chọn phiên từ tab Đang chờ: hiện lịch sử với nhãn người gửi, nút Nhận và ô soạn tắt', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);

    await selectQueueRow('Phiên đang chờ');

    await waitFor(
      () => textOf(desk.harness.container).includes('Đơn của mình đâu?'),
      'lịch sử tin',
    );
    const text = textOf(desk.harness.container);
    expect(text).toContain('Khách');
    expect(text).toContain('Lan - CSKH');
    expect(text).toContain('Hệ thống');
    // Ô soạn tắt khi chưa nhận phiên, kèm chú thích nói rõ lý do.
    expect(composer()?.disabled).toBe(true);
    expect(text).toContain('Nhận hội thoại để trả lời.');
    // Dòng tab Gọi lại không đổi: chưa chọn phiên từ tab đó.
  });

  it('tin staff thiếu tên hiện Nhân viên CSKH', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: {
        'conversation-1': {
          items: [
            staffMessage({ id: 'm1', role: 'staff', body: 'Chào bạn.', staffDisplayName: null }),
          ],
          nextCursor: null,
        },
      },
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');

    await waitFor(() => textOf(desk.harness.container).includes('Nhân viên CSKH'), 'nhãn mặc định');
  });

  it('tin hệ thống chỉ hiện chữ, không vẽ form theo action', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');

    await waitFor(
      () => textOf(desk.harness.container).includes('Phiên đã kết thúc.'),
      'tin hệ thống',
    );
    // Không có ô nhập nào của form gọi lại hay form CSAT trong khung chat phía nhân viên.
    expect(desk.harness.container.querySelectorAll('input, select')).toHaveLength(0);
  });

  it('vẽ dòng phân cách khi sessionId đổi giữa hai tin liền nhau', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: {
        'conversation-1': {
          items: [
            staffMessage({ id: 'm1', sessionId: 'session-1', body: 'Tin phiên cũ.' }),
            staffMessage({
              id: 'm2',
              sessionId: 'session-2',
              body: 'Tin phiên mới.',
              createdAt: '2026-10-22T09:00:00Z',
            }),
          ],
          nextCursor: null,
        },
      },
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');

    await waitFor(() => textOf(desk.harness.container).includes('Tin phiên mới.'), 'tin phiên mới');
    const separators = desk.harness.container.querySelectorAll('[role="separator"]');
    expect(separators).toHaveLength(1);
    const separator = separators[0];
    if (!separator) throw new Error('Thiếu dòng phân cách.');
    // 09:00 UTC là 16:00 giờ Việt Nam, theo định dạng của `formatVietnamDateTime`.
    expect(textOf(separator as HTMLElement)).toBe('Phiên mới, 16:00 22/10/2026');
  });

  it('không vẽ dòng phân cách khi một trong hai tin không có sessionId', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: {
        'conversation-1': {
          items: [
            staffMessage({ id: 'm1', sessionId: null, body: 'Tin trước phiên.' }),
            staffMessage({ id: 'm2', sessionId: 'session-2', body: 'Tin phiên mới.' }),
          ],
          nextCursor: null,
        },
      },
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');

    await waitFor(() => textOf(desk.harness.container).includes('Tin phiên mới.'), 'tin phiên mới');
    expect(desk.harness.container.querySelectorAll('[role="separator"]')).toHaveLength(0);
  });
});

describe('khung chat: nhận phiên', () => {
  it('chọn phiên từ tab Của tôi: không có nút Nhận và ô soạn bật', async () => {
    desk = openDesk(READ_AND_REPLY, {
      mine: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
      cannedResponses: [
        { id: 'ca1', shortcut: 'camon', body: 'Cảm ơn.', shared: true, editable: false },
      ],
    });
    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Khách hỏi về đơn 1'),
      'tab của tôi',
    );
    await selectQueueRow('Phiên của tôi');

    await waitFor(
      () => textOf(desk.harness.container).includes('Đơn của mình đâu?'),
      'lịch sử tin',
    );
    expect(textOf(desk.harness.container)).not.toContain('Nhận hội thoại để trả lời.');
    expect(composer()?.disabled).toBe(false);
  });

  it('bấm Nhận: gọi POST assignment, ô soạn bật ngay và danh sách đọc lại', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');
    await waitFor(() => composer() !== null, 'ô soạn hiện ra');

    const readsBeforeClaim = supportRequests(desk.harness).filter((url) =>
      url.includes('view=waiting'),
    ).length;
    await clickButton(desk.harness.container, 'Nhận');

    await waitFor(() => composer()?.disabled === false, 'ô soạn bật sau khi nhận');
    expect(
      desk.harness.writes.some(
        (write) =>
          write.method === 'POST' && write.url.includes('/api/admin/support/sessions/1/assignment'),
      ),
    ).toBe(true);
    // Đọc lại danh sách phiên sau khi nhận, để dòng phiên chuyển sang tab Của tôi ở lần đọc kế.
    await waitFor(
      () =>
        supportRequests(desk.harness).filter((url) => url.includes('view=waiting')).length >
        readsBeforeClaim,
      'danh sách đọc lại',
    );
  });

  it('lỗi 409 khi nhận phiên: hiện "Đã có người nhận" bằng ErrorAlert', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
      claimConflict: true,
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');
    await waitFor(() => composer() !== null, 'ô soạn hiện ra');

    await clickButton(desk.harness.container, 'Nhận');

    await waitFor(
      () => desk.harness.container.querySelector('[role="alert"]') !== null,
      'thông báo lỗi',
    );
    expect(textOf(desk.harness.container)).toContain('Đã có người nhận');
    // Nhận lỗi thì ô soạn vẫn tắt.
    expect(composer()?.disabled).toBe(true);
  });

  it('lỗi 409 khi nhận phiên: đọc lại danh sách phiên ở hàng chờ', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
      claimConflict: true,
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');
    await waitFor(() => composer() !== null, 'ô soạn hiện ra');

    const readsBefore = supportRequests(desk.harness).filter((url) =>
      url.includes('view=waiting'),
    ).length;
    await clickButton(desk.harness.container, 'Nhận');

    // R1-09: nhận bị 409 phải đọc lại danh sách phiên để người khác nhận rồi thì hàng chờ cập nhật.
    await waitFor(
      () =>
        supportRequests(desk.harness).filter((url) => url.includes('view=waiting')).length >
        readsBefore,
      'danh sách đọc lại sau 409',
    );
  });

  it('lỗi khác khi nhận phiên: hiện câu của API bằng ErrorAlert, ô soạn vẫn tắt và bấm lại được', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
      // R1-04: cờ thật trả lỗi 404 từ route POST assignment, không dựa vào lỗi hàng chờ.
      claimNotFound: true,
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');
    await waitFor(() => composer() !== null, 'ô soạn hiện ra');

    await clickButton(desk.harness.container, 'Nhận');

    await waitFor(
      () => desk.harness.container.querySelector('[role="alert"]') !== null,
      'thông báo lỗi',
    );
    // Kiểm đúng chữ message của API theo R1-04.
    expect(textOf(desk.harness.container)).toContain('Không có phiên.');
    expect(composer()?.disabled).toBe(true);
    // Nút Nhận vẫn còn và bấm lại được khi gặp lỗi khác 409.
    const claimBtn = desk.harness.container.querySelector<HTMLButtonElement>(
      '.support-chat-claim button',
    );
    expect(claimBtn).not.toBeNull();
    expect(claimBtn?.disabled).toBe(false);
  });

  it('chống bấm đôi nút Nhận trong lúc request nhận đang chạy', async () => {
    let resolveClaim: () => void = () => {};
    const claimGate = new Promise<void>((resolve) => {
      resolveClaim = resolve;
    });
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');
    await waitFor(() => composer() !== null, 'ô soạn hiện ra');

    const claimBtn = desk.harness.container.querySelector<HTMLButtonElement>(
      '.support-chat-claim button',
    );
    if (!claimBtn) throw new Error('Thiếu nút Nhận');
    // Chặn fetch assignment lần đầu để nút giữ trạng thái đang nhận.
    const origFetch = globalThis.fetch;
    let postCount = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (url.includes('/assignment') && init?.method === 'POST') {
          postCount++;
          await claimGate;
        }
        return origFetch(input, init);
      }),
    );

    await act(async () => {
      claimBtn.click();
      claimBtn.click();
    });
    expect(claimBtn.disabled).toBe(true);
    expect(postCount).toBe(1);

    resolveClaim();
    await waitFor(() => composer()?.disabled === false, 'nhận xong');
  });
});

describe('khung chat: gửi tin', () => {
  it('gửi thành công: POST messages với body, xóa ô soạn và đọc lại lịch sử', async () => {
    desk = openDesk(READ_AND_REPLY, {
      mine: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Khách hỏi về đơn 1'),
      'tab của tôi',
    );
    await selectQueueRow('Phiên của tôi');
    await waitFor(() => composer()?.disabled === false, 'ô soạn bật');

    const readsBeforeSend = messageRequests().length;
    await act(async () => {
      const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;
      setter.call(composer(), 'Chào bạn, mình kiểm đơn ngay.');
      composer()!.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await act(async () => {
      composer()!.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
      );
    });

    await waitFor(
      () =>
        desk.harness.writes.some(
          (write) =>
            write.method === 'POST' && write.url.includes('/api/admin/support/sessions/1/messages'),
        ),
      'POST gửi tin',
    );
    const write = desk.harness.writes.find((entry) => entry.url.includes('/messages'));
    expect(write?.body).toEqual({ body: 'Chào bạn, mình kiểm đơn ngay.' });
    await waitFor(() => composer()?.value === '', 'ô soạn được dọn sau khi gửi');
    // Backend chỉ phát message.created cho người giữ phiên, nên giao diện tự đọc lại lịch sử.
    expect(messageRequests().length).toBeGreaterThan(readsBeforeSend);
  });

  it('gửi lỗi 403: hiện câu của API, giữ chữ và tắt ô soạn', async () => {
    desk = openDesk(READ_AND_REPLY, {
      mine: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
      sendForbidden: true,
    });
    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Khách hỏi về đơn 1'),
      'tab của tôi',
    );
    await selectQueueRow('Phiên của tôi');
    await waitFor(() => composer()?.disabled === false, 'ô soạn bật');

    await act(async () => {
      const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;
      setter.call(composer(), 'Chào bạn.');
      composer()!.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await act(async () => {
      composer()!.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
      );
    });

    await waitFor(
      () => desk.harness.container.querySelector('[role="alert"]') !== null,
      'thông báo lỗi',
    );
    expect(textOf(desk.harness.container)).toContain(
      'Bạn chưa nhận hội thoại này nên không gửi tin được.',
    );
    expect(composer()?.value).toBe('Chào bạn.');
    expect(composer()?.disabled).toBe(true);
    expect(textOf(desk.harness.container)).toContain('Nhận hội thoại để trả lời.');
  });

  it('sự kiện session.closed của phiên đang mở: ô soạn tắt và nút Nhận biến mất', async () => {
    desk = openDesk(READ_AND_REPLY, {
      mine: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Khách hỏi về đơn 1'),
      'tab của tôi',
    );
    await selectQueueRow('Phiên của tôi');
    await waitFor(() => composer()?.disabled === false, 'ô soạn bật');

    await emitFromServer('session.closed', { sessionId: '1' });

    await waitFor(() => composer()?.disabled === true, 'ô soạn tắt sau khi đóng');
    expect(textOf(desk.harness.container)).toContain('Phiên đã kết thúc.');
    // R1-03: nút Nhận không được hiện khi phiên đã đóng, dù isHeld lúc này bằng false.
    expect(desk.harness.container.querySelectorAll('.support-chat-claim button')).toHaveLength(0);
  });

  it('R1-01: đổi phiên đặt lại nháp và xóa câu lỗi của phiên cũ', async () => {
    desk = openDesk(READ_AND_REPLY, {
      mine: [queueSession('1'), queueSession('2')],
      messagePages: {
        'conversation-1': HISTORY_PAGE,
        'conversation-2': {
          items: [staffMessage({ id: 'm-other', body: 'Tin phiên 2.' })],
          nextCursor: null,
        },
      },
      sendForbidden: true,
    });
    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Khách hỏi về đơn 1'),
      'phiên 1 nạp',
    );

    // Mở phiên 1, gõ nháp và gây lỗi gửi 403.
    const rows = desk.harness.container.querySelectorAll<HTMLButtonElement>(
      '[aria-label="Phiên của tôi"] button',
    );
    await act(async () => rows[0]?.click());
    await waitFor(() => composer() !== null, 'ô soạn phiên 1');

    await act(async () => {
      const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;
      setter.call(composer(), 'Hoàn tiền 500k cho bạn');
      composer()!.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await act(async () => {
      composer()!.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
      );
    });
    await waitFor(
      () => desk.harness.container.querySelector('[role="alert"]') !== null,
      'lỗi hiện ở phiên 1',
    );

    // Bấm phiên 2: ô soạn phải rỗng, không còn câu lỗi của phiên 1 (R1-01).
    const rowsAfter = desk.harness.container.querySelectorAll<HTMLButtonElement>(
      '[aria-label="Phiên của tôi"] button',
    );
    await act(async () => rowsAfter[1]?.click());
    await waitFor(() => textOf(desk.harness.container).includes('Tin phiên 2.'), 'phiên 2 nạp');

    expect(composer()?.value).toBe('');
    expect(desk.harness.container.querySelector('[role="alert"]')).toBeNull();
  });

  it('R1-10: bấm lại đúng dòng đang mở không làm mất trạng thái giữ phiên', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');
    await waitFor(() => composer() !== null, 'ô soạn nạp');
    await clickButton(desk.harness.container, 'Nhận');
    await waitFor(() => composer()?.disabled === false, 'đã nhận phiên');

    // Bấm lại đúng dòng phiên 1 ở tab Đang chờ trước khi danh sách nạp lại: ô soạn vẫn bật.
    await selectQueueRow('Phiên đang chờ');
    expect(composer()?.disabled).toBe(false);
  });

  it('R1-08: lần đọc lại nền bị lỗi thì lịch sử tin đang hiện không bị thay bằng câu lỗi', async () => {
    desk = openDesk(READ_AND_REPLY, {
      mine: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Khách hỏi về đơn 1'),
      'tab của tôi',
    );
    await selectQueueRow('Phiên của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Đơn của mình đâu?'),
      'lịch sử nạp',
    );

    const readsBefore = messageRequests().length;
    // Cờ làm lệnh GET messages kế tiếp trả 404. Phát message.created để kích hoạt đọc lại.
    desk.state.messagesFail = true;
    await emitFromServer('message.created', { sessionId: '1', conversationId: 'conversation-1' });
    // Đợi số request GET messages tăng thêm 1, React Query xong và DOM vẫn giữ tin cũ (R3-07).
    await waitFor(
      () =>
        messageRequests().length > readsBefore &&
        !desk.harness.client.isFetching() &&
        textOf(desk.harness.container).includes('Đơn của mình đâu?'),
      'đọc lại nền đã hoàn tất và vẫn giữ tin cũ',
    );

    // Khung chat không hiện ErrorAlert của QueueResult vì đã có dữ liệu từ trước.
    const queueAlert = desk.harness.container.querySelector('.support-queue-alert');
    expect(queueAlert).toBeNull();
  });

  it('ResizeObserver cuộn xuống đáy khi vùng tin đổi chiều cao (R3-03)', async () => {
    let capturedCallback: ResizeObserverCallback | null = null;
    class FakeResizeObserver implements ResizeObserver {
      constructor(callback: ResizeObserverCallback) {
        capturedCallback = callback;
      }
      observe() {}
      unobserve() {}
      disconnect() {}
    }
    vi.stubGlobal('ResizeObserver', FakeResizeObserver);

    try {
      desk = openDesk(READ_AND_REPLY, {
        mine: [queueSession('1')],
        messagePages: { 'conversation-1': HISTORY_PAGE },
      });
      await openDeskPage(desk);
      await clickButton(desk.harness.container, 'Của tôi');
      await waitFor(
        () => textOf(desk.harness.container).includes('Khách hỏi về đơn 1'),
        'tab của tôi',
      );
      await selectQueueRow('Phiên của tôi');
      await waitFor(
        () => textOf(desk.harness.container).includes('Đơn của mình đâu?'),
        'tin đã nạp',
      );

      const scrollRegion =
        desk.harness.container.querySelector<HTMLDivElement>('.support-chat-scroll');
      if (!scrollRegion) throw new Error('Thiếu vùng cuộn');

      // Giả lập scrollHeight mới sau khi vùng tin co lại.
      Object.defineProperty(scrollRegion, 'scrollHeight', { value: 600, configurable: true });
      expect(capturedCallback).not.toBeNull();

      // Kích hoạt callback của ResizeObserver như khi danh sách mẫu mở ra.
      await act(async () => {
        capturedCallback!([], {} as ResizeObserver);
      });

      expect(scrollRegion.scrollTop).toBe(600);
    } finally {
      vi.unstubAllGlobals();
    }
  });
});

describe('khung chat: sự kiện message.created', () => {
  it('đọc lại lịch sử của hội thoại trong sự kiện, không đọc hội thoại khác', async () => {
    desk = openDesk(READ_AND_REPLY, {
      mine: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Khách hỏi về đơn 1'),
      'tab của tôi',
    );
    await selectQueueRow('Phiên của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Đơn của mình đâu?'),
      'lịch sử tin',
    );

    const readsBefore = messageRequests().length;
    await emitFromServer('message.created', { sessionId: '1', conversationId: 'conversation-2' });
    // Chờ một nhịp để mọi refetch tiềm năng chạy, rồi khẳng định không có refetch nào xảy ra.
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 150));
    });
    expect(messageRequests().length).toBe(readsBefore);

    await emitFromServer('message.created', { sessionId: '1', conversationId: 'conversation-1' });
    await waitFor(
      () => messageRequests().length > readsBefore,
      'đọc lại lịch sử hội thoại đang mở',
    );
  });

  it('dữ liệu sự kiện sai dạng bị bỏ qua', async () => {
    desk = openDesk(READ_AND_REPLY, {
      mine: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Khách hỏi về đơn 1'),
      'tab của tôi',
    );
    await selectQueueRow('Phiên của tôi');
    await waitFor(
      () => textOf(desk.harness.container).includes('Đơn của mình đâu?'),
      'lịch sử tin',
    );

    const readsBefore = messageRequests().length;
    await emitFromServer('message.created', { wrong: true });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 150));
    });
    expect(messageRequests().length).toBe(readsBefore);
  });
});

describe('khung chat: quyền', () => {
  it('người chỉ có conversations:read không thấy nút Nhận, ô soạn tắt và không gọi route mẫu', async () => {
    desk = openDesk(READ_ONLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');

    await waitFor(
      () => textOf(desk.harness.container).includes('Đơn của mình đâu?'),
      'lịch sử tin',
    );
    expect(textOf(desk.harness.container)).not.toContain('Nhận hội thoại để trả lời.');
    expect(composer()?.disabled).toBe(true);
    expect(textOf(desk.harness.container)).toContain('Bạn chỉ có quyền xem hội thoại.');
    expect(supportRequests(desk.harness).some((url) => url.includes('canned-responses'))).toBe(
      false,
    );
  });

  it('khi phiên bị đóng thì không hiện nhóm nút Chuyển và Kết thúc, ô soạn tin bị tắt', async () => {
    desk = openDesk(READ_AND_REPLY, {
      mine: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await selectQueueRow('Phiên của tôi');

    await waitFor(
      () => desk.harness.container.querySelector('.support-chat-actions') !== null,
      'nút thao tác phiên',
    );
    expect(textOf(desk.harness.container)).toContain('Chuyển');
    expect(textOf(desk.harness.container)).toContain('Kết thúc');
    expect(composer()?.disabled).toBe(false);

    // Phát sự kiện session.closed cho phiên đang mở.
    await emitFromServer('session.closed', { sessionId: '1' });

    // Khi phiên đã đóng, nhóm nút thao tác biến mất và ô soạn bị tắt.
    await waitFor(
      () => desk.harness.container.querySelector('.support-chat-actions') === null,
      'nhóm nút thao tác biến mất',
    );
    expect(composer()?.disabled).toBe(true);
    expect(textOf(desk.harness.container)).toContain('Phiên đã kết thúc.');
  });

  it('người chỉ có conversations:read mở phiên của mình không thấy nút Chuyển và Kết thúc, ô soạn bị tắt', async () => {
    desk = openDesk(READ_ONLY, {
      mine: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await clickButton(desk.harness.container, 'Của tôi');
    await selectQueueRow('Phiên của tôi');

    await waitFor(
      () => textOf(desk.harness.container).includes('Đơn của mình đâu?'),
      'lịch sử tin',
    );

    // Không hiện nhóm nút Chuyển và Kết thúc vì người dùng không có quyền phản hồi.
    expect(desk.harness.container.querySelector('.support-chat-actions')).toBeNull();
    expect(textOf(desk.harness.container)).not.toContain('Chuyển');
    expect(textOf(desk.harness.container)).not.toContain('Kết thúc');
    expect(composer()?.disabled).toBe(true);
    expect(textOf(desk.harness.container)).toContain('Bạn chỉ có quyền xem hội thoại.');
  });

  it('phiên đang chờ chỉ hiện nút Nhận, không hiện nhóm nút Chuyển và Kết thúc', async () => {
    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');

    await waitFor(
      () => textOf(desk.harness.container).includes('Đơn của mình đâu?'),
      'lịch sử tin',
    );

    // Phiên đang chờ chưa được nhận (isHeld=false): có nút Nhận nhưng không có nhóm Chuyển và Kết thúc.
    expect(desk.harness.container.querySelector('.support-chat-claim button')).not.toBeNull();
    expect(textOf(desk.harness.container)).toContain('Nhận');
    expect(desk.harness.container.querySelector('.support-chat-actions')).toBeNull();
    expect(textOf(desk.harness.container)).not.toContain('Chuyển');
    expect(textOf(desk.harness.container)).not.toContain('Kết thúc');
  });

  it('không hiện nhóm nút Chuyển và Kết thúc khi API nhận trả về sau sự kiện session.closed', async () => {
    let resolveClaim: (assignment: SessionAssignment) => void = () => {};
    const claimPromise = new Promise<SessionAssignment>((resolve) => {
      resolveClaim = resolve;
    });
    vi.spyOn(supportApi, 'claimSupportSession').mockReturnValueOnce(claimPromise);

    desk = openDesk(READ_AND_REPLY, {
      waiting: [queueSession('1')],
      messagePages: { 'conversation-1': HISTORY_PAGE },
    });
    await openDeskPage(desk);
    await selectQueueRow('Phiên đang chờ');
    await waitFor(
      () => desk.harness.container.querySelector('.support-chat-claim button') !== null,
      'nút nhận phiên',
    );

    // Bấm nhận phiên: request nhận phiên bắt đầu và đang chờ máy chủ trả lời.
    await clickButton(desk.harness.container, 'Nhận');

    // Máy chủ gửi sự kiện phiên đã bị đóng trước khi request nhận phản hồi xong.
    await emitFromServer('session.closed', { sessionId: '1' });

    // Sau đó request nhận phiên hoàn tất (đặt isHeld=true lên phiên đã isClosed=true).
    await act(async () => {
      resolveClaim({
        sessionId: '1',
        staffId: 'staff-0001',
        claimedAt: '2026-10-22T08:05:00Z',
      });
    });

    // Nhóm nút Chuyển và Kết thúc không được xuất hiện vì phiên đã đóng.
    expect(desk.harness.container.querySelector('.support-chat-actions')).toBeNull();
    expect(textOf(desk.harness.container)).not.toContain('Chuyển');
    expect(textOf(desk.harness.container)).not.toContain('Kết thúc');
    expect(composer()?.disabled).toBe(true);
    expect(textOf(desk.harness.container)).toContain('Phiên đã kết thúc.');
  });
});
