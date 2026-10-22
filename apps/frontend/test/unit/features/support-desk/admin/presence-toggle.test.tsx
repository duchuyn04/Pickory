// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { waitFor } from '../../identity/admin/admin-list-test-kit';
import {
  READ_AND_REPLY,
  READ_ONLY,
  STAFF_ID,
  activeEventSource,
  findSwitch,
  openDesk,
  openDeskPage,
  textOf,
  type OpenedDesk,
} from './support-desk-test-kit';

let desk: OpenedDesk;

afterEach(async () => {
  vi.useRealTimers();
  await desk.harness.cleanup();
});

function presenceSwitch(): HTMLButtonElement | null {
  return findSwitch(desk.harness.container, 'Sẵn sàng');
}

// Công tắc dùng `aria-disabled` thay cho `disabled`, để focus bàn phím không rơi mất lúc đang lưu.
function isPresenceReady(): boolean {
  const toggle = presenceSwitch();
  return toggle !== null && toggle.getAttribute('aria-disabled') !== 'true';
}

async function clickPresenceSwitch() {
  await act(async () => presenceSwitch()?.click());
}

// Số lần `GET` danh sách sẵn sàng đã gửi, kể cả lần đang bị giữ.
function countPresenceReads(): number {
  return desk.harness.requestedUrls.filter((url) => url === '/api/admin/support/presence').length;
}

function presenceWrites() {
  return desk.harness.writes.filter((write) => write.url === '/api/admin/support/presence');
}

describe('PresenceToggle', () => {
  it('hiện trên thanh trên cùng khi người dùng có conversations:reply', async () => {
    desk = openDesk(READ_AND_REPLY);

    await openDeskPage(desk);

    const topBar = desk.harness.container.querySelector('.admin-top');
    expect(topBar?.contains(presenceSwitch())).toBe(true);
  });

  it('không hiện khi người dùng chỉ có conversations:read', async () => {
    desk = openDesk(READ_ONLY);

    await openDeskPage(desk);

    expect(presenceSwitch()).toBeNull();
  });

  it('hiện Tạm vắng và aria-checked false khi người dùng không có trong danh sách sẵn sàng', async () => {
    desk = openDesk(READ_AND_REPLY, { availableStaffIds: ['staff-khac'] });

    await openDeskPage(desk);
    await waitFor(() => isPresenceReady(), 'công tắc sẵn sàng dùng được');

    expect(presenceSwitch()?.getAttribute('aria-checked')).toBe('false');
    expect(textOf(desk.harness.container)).toContain('Tạm vắng');
  });

  it('hiện bật khi người dùng có trong danh sách sẵn sàng', async () => {
    desk = openDesk(READ_AND_REPLY, { availableStaffIds: [STAFF_ID] });

    await openDeskPage(desk);

    await waitFor(() => presenceSwitch()?.getAttribute('aria-checked') === 'true', 'công tắc bật');
  });

  it('bấm công tắc gửi PUT available rồi mới bật', async () => {
    desk = openDesk(READ_AND_REPLY);
    await openDeskPage(desk);
    await waitFor(() => isPresenceReady(), 'công tắc sẵn sàng dùng được');

    await clickPresenceSwitch();

    await waitFor(() => presenceSwitch()?.getAttribute('aria-checked') === 'true', 'công tắc bật');
    expect(presenceWrites().map((write) => write.body)).toEqual([{ status: 'available' }]);
  });

  it('bấm công tắc đang bật gửi PUT away', async () => {
    desk = openDesk(READ_AND_REPLY, { availableStaffIds: [STAFF_ID] });
    await openDeskPage(desk);
    await waitFor(() => presenceSwitch()?.getAttribute('aria-checked') === 'true', 'công tắc bật');

    await clickPresenceSwitch();

    await waitFor(() => presenceSwitch()?.getAttribute('aria-checked') === 'false', 'công tắc tắt');
    expect(presenceWrites().map((write) => write.body)).toEqual([{ status: 'away' }]);
  });

  it('PUT lỗi thì giữ trạng thái cũ và báo lỗi ngắn', async () => {
    desk = openDesk(READ_AND_REPLY, { putFails: true });
    await openDeskPage(desk);
    await waitFor(() => isPresenceReady(), 'công tắc sẵn sàng dùng được');

    await clickPresenceSwitch();

    await waitFor(
      () => desk.harness.container.querySelector('.admin-top [role="alert"]') !== null,
      'thông báo lỗi',
    );
    expect(presenceSwitch()?.getAttribute('aria-checked')).toBe('false');
    expect(textOf(desk.harness.container.querySelector('.admin-top') as HTMLElement)).toContain(
      'Không đổi được trạng thái',
    );
  });

  it('không đổi công tắc trước khi API trả lời', async () => {
    let releasePut: (() => void) | undefined;
    desk = openDesk(READ_AND_REPLY);
    const originalFetch = globalThis.fetch;
    // Giữ lời hứa của `PUT` để quan sát công tắc lúc API chưa trả lời.
    globalThis.fetch = ((input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'PUT') {
        return new Promise<Response>((resolve) => {
          releasePut = () =>
            resolve(
              new Response(
                JSON.stringify({
                  data: {
                    staffId: STAFF_ID,
                    status: 'available',
                    lastSeenAt: '2026-10-22T08:00:00Z',
                  },
                }),
              ),
            );
        });
      }
      return originalFetch(input, init);
    }) as typeof fetch;
    await openDeskPage(desk);
    await waitFor(() => isPresenceReady(), 'công tắc sẵn sàng dùng được');

    await clickPresenceSwitch();
    expect(presenceSwitch()?.getAttribute('aria-checked')).toBe('false');

    await act(async () => releasePut?.());
    await waitFor(() => presenceSwitch()?.getAttribute('aria-checked') === 'true', 'công tắc bật');
    globalThis.fetch = originalFetch;
  });

  it('cập nhật công tắc khi nhận presence.changed của chính mình, ví dụ job đổi sang away', async () => {
    desk = openDesk(READ_AND_REPLY, { availableStaffIds: [STAFF_ID] });
    await openDeskPage(desk);
    await waitFor(() => presenceSwitch()?.getAttribute('aria-checked') === 'true', 'công tắc bật');

    await act(async () =>
      activeEventSource().emit('presence.changed', { staffId: STAFF_ID, status: 'away' }),
    );

    await waitFor(() => presenceSwitch()?.getAttribute('aria-checked') === 'false', 'công tắc tắt');
  });

  it('bỏ qua presence.changed của đồng nghiệp', async () => {
    desk = openDesk(READ_AND_REPLY, { availableStaffIds: [STAFF_ID] });
    await openDeskPage(desk);
    await waitFor(() => presenceSwitch()?.getAttribute('aria-checked') === 'true', 'công tắc bật');

    await act(async () =>
      activeEventSource().emit('presence.changed', { staffId: 'staff-khac', status: 'away' }),
    );

    expect(presenceSwitch()?.getAttribute('aria-checked')).toBe('true');
  });

  it('bỏ qua presence.changed sai dạng', async () => {
    desk = openDesk(READ_AND_REPLY, { availableStaffIds: [STAFF_ID] });
    await openDeskPage(desk);
    await waitFor(() => presenceSwitch()?.getAttribute('aria-checked') === 'true', 'công tắc bật');

    await act(async () => activeEventSource().emit('presence.changed', { oops: true }));

    expect(presenceSwitch()?.getAttribute('aria-checked')).toBe('true');
  });

  it('đọc lại trạng thái sẵn sàng khi luồng SSE nối lại và mở lần nữa, vì sự kiện lúc đứt đã mất', async () => {
    desk = openDesk(READ_AND_REPLY);
    await openDeskPage(desk);
    await waitFor(isPresenceReady, 'công tắc sẵn sàng dùng được');
    await act(async () => activeEventSource().open());
    expect(presenceSwitch()?.getAttribute('aria-checked')).toBe('false');

    // Chỉ làm giả `setTimeout` để chạy hẹn giờ nối lại ngay. `shouldAdvanceTime` giữ đồng hồ chạy cho `waitFor`.
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'], shouldAdvanceTime: true });
    await act(async () => {
      activeEventSource().failPermanently();
      vi.advanceTimersByTime(5000);
    });
    // Trong lúc luồng đứt, một thiết bị khác của cùng nhân viên bật "Sẵn sàng". Sự kiện đó đã mất.
    desk.state.availableStaffIds = [STAFF_ID];
    await act(async () => activeEventSource().open());

    await waitFor(
      () => presenceSwitch()?.getAttribute('aria-checked') === 'true',
      'công tắc bật sau khi luồng mở lại',
    );
  });

  it('lần đọc danh sách cũ về muộn không ghi đè trạng thái vừa lưu bằng PUT', async () => {
    desk = openDesk(READ_AND_REPLY);
    await openDeskPage(desk);
    await waitFor(isPresenceReady, 'công tắc sẵn sàng dùng được');

    // Luồng mở làm danh sách được đọc lại. Máy chủ giữ câu trả lời cũ (chưa ai sẵn sàng) cho tới khi test thả.
    let releaseStaleRead: () => void = () => {};
    desk.state.presenceGate = new Promise<void>((resolve) => {
      releaseStaleRead = resolve;
    });
    await act(async () => activeEventSource().open());

    // `PUT` không bị giữ, nên xong trước lần đọc cũ.
    await clickPresenceSwitch();
    await waitFor(() => presenceSwitch()?.getAttribute('aria-checked') === 'true', 'công tắc bật');

    await act(async () => releaseStaleRead());
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 100));
    });

    expect(presenceSwitch()?.getAttribute('aria-checked')).toBe('true');
  });

  it('PUT lỗi thì báo lỗi ngay và đọc lại danh sách đã bị hủy, không chờ lần đọc về', async () => {
    desk = openDesk(READ_AND_REPLY, { putFails: true });
    await openDeskPage(desk);
    await waitFor(isPresenceReady, 'công tắc sẵn sàng dùng được');

    // Giữ câu trả lời của lần đọc do luồng mở kích hoạt. Bấm công tắc sẽ hủy lần đọc này.
    let releaseHeldRead: () => void = () => {};
    desk.state.presenceGate = new Promise<void>((resolve) => {
      releaseHeldRead = resolve;
    });
    try {
      await act(async () => activeEventSource().open());
      await waitFor(() => countPresenceReads() === 2, 'lần đọc do luồng mở đã được gửi');

      await clickPresenceSwitch();

      // Lần đọc thứ ba là lần đọc lại sau lỗi của `PUT`. Thiếu nó, danh sách giữ dữ liệu từ trước lúc luồng đứt.
      await waitFor(() => countPresenceReads() === 3, 'đọc lại sau khi PUT lỗi');

      // Lần đọc lại vẫn đang bị giữ. Dòng lỗi phải hiện và công tắc phải mở lại ngay, không chờ lần đọc đó.
      await waitFor(
        () => desk.harness.container.querySelector('.admin-top [role="alert"]') !== null,
        'dòng lỗi hiện khi lần đọc lại còn đang chờ',
      );
      expect(isPresenceReady()).toBe(true);
    } finally {
      // Thả câu trả lời bị giữ cả khi phép kiểm ở trên hết giờ, để không còn lời hứa treo sang test sau.
      await act(async () => releaseHeldRead());
    }
  });
});
