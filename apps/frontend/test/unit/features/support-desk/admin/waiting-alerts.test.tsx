// @vitest-environment jsdom
import { act } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { clickButton, waitFor } from '../../identity/admin/admin-list-test-kit';
import {
  FakeAudioContext,
  FakeNotification,
  READ_AND_REPLY,
  READ_ONLY,
  STAFF_ID,
  activeEventSource,
  findSwitch,
  installFakeNotification,
  openDesk,
  openDeskPage,
  queueSession,
  textOf,
  type OpenedDesk,
} from './support-desk-test-kit';

let desk: OpenedDesk;

afterEach(async () => {
  await desk.harness.cleanup();
});

const SOUND_STORAGE_KEY = 'pickory.support.soundEnabled';

function soundSwitch(): HTMLButtonElement | null {
  return findSwitch(desk.harness.container, 'Âm báo');
}

// Một thao tác bất kỳ của người dùng trên trang, đủ để trình duyệt cho phép tạo `AudioContext`.
async function userGesture() {
  await act(async () => {
    desk.harness.container.querySelector<HTMLElement>('h1')?.click();
  });
}

async function customerWaits(sessionId: string) {
  await act(async () => activeEventSource().emit('session.waiting', { sessionId }));
}

async function openAvailableDesk() {
  desk = openDesk(READ_AND_REPLY, { availableStaffIds: [STAFF_ID], waiting: [queueSession('1')] });
  return desk;
}

async function waitUntilAvailable() {
  await waitFor(
    () => findSwitch(desk.harness.container, 'Sẵn sàng')?.getAttribute('aria-checked') === 'true',
    'nhân viên sẵn sàng',
  );
}

describe('thông báo trình duyệt', () => {
  it('không hỏi quyền lúc tải trang, chỉ hỏi sau khi bấm Bật thông báo', async () => {
    installFakeNotification('default');
    desk = openDesk(READ_AND_REPLY);

    await openDeskPage(desk);
    expect(FakeNotification.requested).toBe(0);

    await clickButton(desk.harness.container, 'Bật thông báo');

    expect(FakeNotification.requested).toBe(1);
    await waitFor(() => textOf(desk.harness.container).includes('Thông báo đã bật'), 'đã bật');
  });

  it('ẩn nút khi quyền đã được cấp', async () => {
    installFakeNotification('granted');
    desk = openDesk(READ_AND_REPLY);

    await openDeskPage(desk);

    expect(textOf(desk.harness.container)).toContain('Thông báo đã bật');
    expect(
      [...desk.harness.container.querySelectorAll('button')].some(
        (button) => button.textContent === 'Bật thông báo',
      ),
    ).toBe(false);
  });

  it('báo trình duyệt đang chặn khi quyền bị từ chối, và không có nút bật', async () => {
    installFakeNotification('denied');
    desk = openDesk(READ_AND_REPLY);

    await openDeskPage(desk);

    expect(textOf(desk.harness.container)).toContain('Trình duyệt đang chặn thông báo');
    expect(FakeNotification.requested).toBe(0);
  });

  it('không hiện nút khi trình duyệt không hỗ trợ Notification', async () => {
    desk = openDesk(READ_AND_REPLY);

    await openDeskPage(desk);

    expect(textOf(desk.harness.container)).not.toContain('Bật thông báo');
  });

  it('hiện thông báo khi nhận session.waiting lúc đang sẵn sàng', async () => {
    installFakeNotification('granted');
    await openAvailableDesk();
    await openDeskPage(desk);
    await waitUntilAvailable();

    await customerWaits('2');

    expect(FakeNotification.shown).toHaveLength(1);
    expect(FakeNotification.shown[0]?.title).toBe('Có khách đang chờ');
  });

  it('không hiện thông báo khi nhân viên đang Tạm vắng', async () => {
    installFakeNotification('granted');
    desk = openDesk(READ_AND_REPLY, { availableStaffIds: [] });
    await openDeskPage(desk);

    await customerWaits('2');

    expect(FakeNotification.shown).toHaveLength(0);
  });

  it('không hiện thông báo cho sự kiện khác session.waiting', async () => {
    installFakeNotification('granted');
    await openAvailableDesk();
    await openDeskPage(desk);
    await waitUntilAvailable();

    await act(async () => activeEventSource().emit('session.assigned', { sessionId: '1' }));
    await act(async () => activeEventSource().emit('session.closed', { sessionId: '1' }));

    expect(FakeNotification.shown).toHaveLength(0);
  });
});

describe('âm báo', () => {
  it('mặc định bật', async () => {
    desk = openDesk(READ_AND_REPLY);

    await openDeskPage(desk);

    expect(soundSwitch()?.getAttribute('aria-checked')).toBe('true');
  });

  it('không tạo AudioContext lúc tải trang hay khi nhận sự kiện trước mọi thao tác', async () => {
    await openAvailableDesk();
    await openDeskPage(desk);
    await waitUntilAvailable();

    await customerWaits('2');

    expect(FakeAudioContext.instances).toHaveLength(0);
    expect(FakeAudioContext.started).toBe(0);
  });

  it('phát tiếng bíp khi nhận session.waiting sau một thao tác của người dùng', async () => {
    await openAvailableDesk();
    await openDeskPage(desk);
    await waitUntilAvailable();
    await userGesture();
    expect(FakeAudioContext.instances).toHaveLength(1);

    await customerWaits('2');

    expect(FakeAudioContext.started).toBeGreaterThan(0);
  });

  it('không phát tiếng khi nhân viên đang Tạm vắng', async () => {
    desk = openDesk(READ_AND_REPLY, { availableStaffIds: [] });
    await openDeskPage(desk);
    await userGesture();

    await customerWaits('2');

    expect(FakeAudioContext.started).toBe(0);
  });

  it('không phát tiếng khi sự kiện không phải session.waiting', async () => {
    await openAvailableDesk();
    await openDeskPage(desk);
    await waitUntilAvailable();
    await userGesture();

    await act(async () => activeEventSource().emit('session.closed', { sessionId: '1' }));

    expect(FakeAudioContext.started).toBe(0);
  });

  it('tắt âm báo thì ghi vào localStorage và không phát tiếng nữa', async () => {
    await openAvailableDesk();
    await openDeskPage(desk);
    await waitUntilAvailable();
    await act(async () => soundSwitch()?.click());

    expect(soundSwitch()?.getAttribute('aria-checked')).toBe('false');
    expect(localStorage.getItem(SOUND_STORAGE_KEY)).toBe('false');

    await customerWaits('2');

    expect(FakeAudioContext.started).toBe(0);
  });

  it('đọc lựa chọn tắt đã lưu khi tải trang', async () => {
    desk = openDesk(READ_AND_REPLY);
    localStorage.setItem(SOUND_STORAGE_KEY, 'false');

    await openDeskPage(desk);

    expect(soundSwitch()?.getAttribute('aria-checked')).toBe('false');
  });

  it('bật lại âm báo bằng cách bấm công tắc thì tạo AudioContext và phát được tiếng', async () => {
    await openAvailableDesk();
    localStorage.setItem(SOUND_STORAGE_KEY, 'false');
    await openDeskPage(desk);
    await waitUntilAvailable();
    expect(FakeAudioContext.instances).toHaveLength(0);

    await act(async () => soundSwitch()?.click());
    expect(soundSwitch()?.getAttribute('aria-checked')).toBe('true');
    expect(FakeAudioContext.instances).toHaveLength(1);

    await customerWaits('2');
    expect(FakeAudioContext.started).toBeGreaterThan(0);
  });

  it('người chỉ có conversations:read không thấy công tắc âm báo và nút bật thông báo, vì không bao giờ nhận được báo', async () => {
    installFakeNotification('default');
    desk = openDesk(READ_ONLY);

    await openDeskPage(desk);

    expect(findSwitch(desk.harness.container, 'Sẵn sàng')).toBeNull();
    expect(soundSwitch()).toBeNull();
    expect(textOf(desk.harness.container)).not.toContain('Bật thông báo');
  });

  it('người chỉ có conversations:read không bị gắn bộ nghe thao tác để tạo AudioContext', async () => {
    desk = openDesk(READ_ONLY);
    await openDeskPage(desk);

    await userGesture();

    expect(FakeAudioContext.instances).toHaveLength(0);
  });
});
