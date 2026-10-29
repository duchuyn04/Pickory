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
