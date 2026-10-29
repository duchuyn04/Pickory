import type { EventSourceLike } from '../../../../../src/features/support-desk/admin/support-event-stream';

const CONNECTING = 0;
const OPEN = 1;
const CLOSED = 2;

// `EventSource` giả dùng chung cho mọi test của màn CSKH. Test quyết định lúc nào luồng mở, lỗi hay nhận sự kiện.
export class FakeEventSource implements EventSourceLike {
  static instances: FakeEventSource[] = [];

  readyState = CONNECTING;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closeCalls = 0;
  private readonly listeners = new Map<string, (event: { data: string }) => void>();

  constructor(public readonly url: string) {
    FakeEventSource.instances.push(this);
  }

  addEventListener(name: string, listener: (event: { data: string }) => void) {
    this.listeners.set(name, listener);
  }

  close() {
    this.closeCalls += 1;
    this.readyState = CLOSED;
  }

  open() {
    this.readyState = OPEN;
    this.onopen?.();
  }

  // Trình duyệt tự nối lại: `readyState` về CONNECTING và `onerror` chạy.
  dropAndReconnectByBrowser() {
    this.readyState = CONNECTING;
    this.onerror?.();
  }

  // Lỗi HTTP: trình duyệt đóng hẳn luồng và không tự nối lại.
  failPermanently() {
    this.readyState = CLOSED;
    this.onerror?.();
  }

  // Phát sự kiện với dữ liệu đã được mã hóa JSON.
  emit(name: string, data: unknown) {
    this.emitRaw(name, JSON.stringify(data));
  }

  // Phát sự kiện với chuỗi thô, để test dữ liệu hỏng.
  emitRaw(name: string, rawData: string) {
    this.listeners.get(name)?.({ data: rawData });
  }
}
