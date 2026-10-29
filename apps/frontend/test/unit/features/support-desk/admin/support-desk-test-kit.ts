import { vi } from 'vitest';
import {
  actorWith,
  createHarness,
  waitFor,
  type AdminListHarness,
} from '../../identity/admin/admin-list-test-kit';
import { FakeEventSource } from './fake-event-source';

export { FakeEventSource };

// Bộ dựng chung cho test màn CSKH: giả ba API của trình duyệt (`EventSource`, `Notification`, `AudioContext`) và các route của support desk.

export const STAFF_ID = 'user-staff';
export const READ_ONLY = ['conversations:read'];
export const READ_AND_REPLY = ['conversations:read', 'conversations:reply'];

// React StrictMode mở rồi đóng luồng một lần lúc dựng. Chỉ luồng chưa đóng mới còn hoạt động.
export function activeEventSources(): FakeEventSource[] {
  return FakeEventSource.instances.filter((source) => source.closeCalls === 0);
}

export function activeEventSource(): FakeEventSource {
  const [source] = activeEventSources();
  if (!source) throw new Error('Chưa có luồng sự kiện nào đang mở.');
  return source;
}

export function installFakeEventSource() {
  FakeEventSource.instances = [];
  vi.stubGlobal('EventSource', FakeEventSource);
}

// `AudioContext` giả ghi lại số lần tạo và số tiếng bíp đã phát.
export class FakeAudioContext {
  static instances: FakeAudioContext[] = [];
  static started = 0;

  state = 'running';
  currentTime = 0;
  destination = {};

  constructor() {
    FakeAudioContext.instances.push(this);
  }

  resume() {
    return Promise.resolve();
  }

  close() {
    this.state = 'closed';
    return Promise.resolve();
  }

  createGain() {
    return {
      gain: {
        setValueAtTime: () => {},
        linearRampToValueAtTime: () => {},
        exponentialRampToValueAtTime: () => {},
      },
      connect: () => {},
    };
  }

  createOscillator() {
    return {
      type: 'sine',
      frequency: { setValueAtTime: () => {} },
      connect: () => {},
      start: () => {
        FakeAudioContext.started += 1;
      },
      stop: () => {},
    };
  }
}

export function installFakeAudioContext() {
  FakeAudioContext.instances = [];
  FakeAudioContext.started = 0;
  vi.stubGlobal('AudioContext', FakeAudioContext);
}

// `Notification` giả. `requestPermission` và các thông báo đã hiện được ghi lại để test kiểm.
export class FakeNotification {
  static permission: NotificationPermission = 'default';
  static requested = 0;
  static shown: { title: string; options?: NotificationOptions }[] = [];

  constructor(title: string, options?: NotificationOptions) {
    FakeNotification.shown.push({ title, options });
  }

  static requestPermission() {
    FakeNotification.requested += 1;
    FakeNotification.permission = 'granted';
    return Promise.resolve<NotificationPermission>('granted');
  }
}

export function installFakeNotification(permission: NotificationPermission = 'default') {
  FakeNotification.permission = permission;
  FakeNotification.requested = 0;
  FakeNotification.shown = [];
  vi.stubGlobal('Notification', FakeNotification);
}

// Dữ liệu mà các route của support desk trả về. Test sửa trực tiếp để mô phỏng thay đổi từ máy chủ.
export interface DeskState {
  waiting: unknown[];
  mine: unknown[];
  callbacks: unknown[];
  availableStaffIds: string[];
  // Route chưa có ở backend trả 404.
  waitingMissing: boolean;
  // Lệnh `PUT` trạng thái trả lỗi 500.
  putFails: boolean;
  // Có giá trị này thì lệnh `GET` danh sách sẵn sàng chỉ trả lời sau khi lời hứa xong, kèm danh sách tại lúc gọi.
  presenceGate?: Promise<void>;
}

export function queueSession(id: string, preview = `Khách hỏi về đơn ${id}`) {
  return {
    id,
    conversationId: `conversation-${id}`,
    preview,
    waitingSeconds: 90,
    handoffAt: '2026-10-22T08:00:00Z',
  };
}

export function callbackRequest(id: string, status: string) {
  return { id, phone: `0900000${id.padStart(3, '0')}`, status, createdAt: '2026-10-22T08:00:00Z' };
}

function notFound() {
  return new Response(
    JSON.stringify({ error: { code: 'NOT_FOUND', message: 'Không tìm thấy.' } }),
    {
      status: 404,
    },
  );
}

function presenceEntry(staffId: string) {
  return { staffId, status: 'available', lastSeenAt: '2026-10-22T08:00:00Z' };
}

export interface OpenedDesk {
  harness: AdminListHarness;
  state: DeskState;
}

// Dựng màn CSKH với quyền cho trước. Các route của support desk đọc và ghi vào `state`.
export function openDesk(
  permissions: string[],
  stateOverrides: Partial<DeskState> = {},
): OpenedDesk {
  installFakeEventSource();
  installFakeAudioContext();
  const state: DeskState = {
    waiting: [],
    mine: [],
    callbacks: [],
    availableStaffIds: [],
    waitingMissing: false,
    putFails: false,
    ...stateOverrides,
  };

  const harness = createHarness(actorWith(permissions), (requested, init) => {
    const url = new URL(requested, 'http://localhost');
    const method = init?.method ?? 'GET';

    if (url.pathname === '/api/admin/support/presence') {
      if (method !== 'PUT') {
        const listAtRequestTime = state.availableStaffIds.map(presenceEntry);
        return state.presenceGate
          ? state.presenceGate.then(() => listAtRequestTime)
          : listAtRequestTime;
      }
      if (state.putFails) {
        return new Response(
          JSON.stringify({ error: { code: 'INTERNAL', message: 'Máy chủ gặp lỗi.' } }),
          { status: 500 },
        );
      }
      const { status } = JSON.parse(String(init?.body)) as { status: string };
      state.availableStaffIds = state.availableStaffIds.filter((id) => id !== STAFF_ID);
      if (status === 'available') state.availableStaffIds.push(STAFF_ID);
      return { staffId: STAFF_ID, status, lastSeenAt: '2026-10-22T08:05:00Z' };
    }
    if (url.pathname === '/api/admin/support/sessions') {
      const view = url.searchParams.get('view');
      if (view === 'waiting') return state.waitingMissing ? notFound() : state.waiting;
      return state.mine;
    }
    if (url.pathname === '/api/admin/support/callback-requests') return state.callbacks;
    return [];
  });
  return { harness, state };
}

// Trang CSKH chờ đủ quyền và dữ liệu đầu tiên. Cột hàng chờ có nhãn "Hàng chờ".
export async function openDeskPage(desk: OpenedDesk) {
  await desk.harness.render('/admin/conversations');
  await waitFor(
    () => desk.harness.container.querySelector('[aria-label="Hàng chờ"]') !== null,
    'cột hàng chờ hiện ra',
  );
}

export function textOf(container: HTMLElement): string {
  return container.textContent ?? '';
}

export function findSwitch(container: HTMLElement, label: string): HTMLButtonElement | null {
  return (
    [...container.querySelectorAll<HTMLButtonElement>('[role="switch"]')].find((element) =>
      element.textContent?.trim().startsWith(label),
    ) ?? null
  );
}

export function findTab(container: HTMLElement, label: string): HTMLButtonElement | null {
  return (
    [...container.querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((element) =>
      element.textContent?.startsWith(label),
    ) ?? null
  );
}

export function supportRequests(harness: AdminListHarness): string[] {
  return harness.requestedUrls.filter((url) => url.startsWith('/api/admin/support'));
}
