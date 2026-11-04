// Ba trạng thái hiển thị của luồng. `lost` chỉ có khi luồng không mở quá 5 giây.
export type StreamStatus = 'connecting' | 'open' | 'lost';

// Phần của `EventSource` mà lớp này dùng. Test thay bằng bản giả có cùng hình dạng.
export interface EventSourceLike {
  readonly readyState: number;
  onopen: ((event: Event) => void) | null;
  onerror: ((event: Event) => void) | null;
  addEventListener(name: string, listener: (event: { data: unknown }) => void): void;
  close(): void;
}

export interface SupportEventStreamOptions {
  url: string;
  // Chỉ các sự kiện có tên trong danh sách này được chuyển cho `onEvent`.
  eventNames: readonly string[];
  createEventSource?: (url: string) => EventSourceLike;
}

export interface SupportEventStreamListener {
  onStatusChange: (status: StreamStatus) => void;
  // Chạy mỗi lần luồng mở, kể cả lần mở lại. Nơi nhận dùng nó để đọc lại dữ liệu.
  onOpen: () => void;
  onEvent: (name: string, data: unknown) => void;
}

// Luồng đóng đều đặn mỗi 5 phút và trình duyệt nối lại sau vài giây. Chờ 5 giây trước khi báo mất kết nối.
const LOST_AFTER_MS = 5000;
const INITIAL_RETRY_MS = 5000;
const MAX_RETRY_MS = 30_000;
// Giá trị `EventSource.CLOSED`. Môi trường test không có lớp `EventSource` để đọc hằng này.
const EVENT_SOURCE_CLOSED = 2;

function createBrowserEventSource(url: string): EventSourceLike {
  return new EventSource(url);
}

// Dữ liệu sự kiện là JSON. Dữ liệu hỏng thành `null`, để nơi nhận vẫn biết sự kiện đã đến.
function parseEventData(rawData: unknown): unknown {
  if (typeof rawData !== 'string') return null;
  try {
    return JSON.parse(rawData);
  } catch {
    return null;
  }
}

// Giữ một kết nối SSE tới máy chủ. Lớp này tự nối lại khi trình duyệt bỏ cuộc và báo trạng thái cho giao diện.
export class SupportEventStream {
  private currentStatus: StreamStatus = 'connecting';
  private eventSource: EventSourceLike | null = null;
  private lostTimer: ReturnType<typeof setTimeout> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private nextRetryDelayMs = INITIAL_RETRY_MS;
  private readonly createEventSource: (url: string) => EventSourceLike;

  constructor(
    private readonly options: SupportEventStreamOptions,
    private readonly listener: SupportEventStreamListener,
  ) {
    this.createEventSource = options.createEventSource ?? createBrowserEventSource;
  }

  get status(): StreamStatus {
    return this.currentStatus;
  }

  start(): void {
    this.setStatus('connecting');
    this.nextRetryDelayMs = INITIAL_RETRY_MS;
    this.connect();
    // Đếm 5 giây ngay từ lúc bắt đầu. API không mở được luồng thì giao diện phải báo mất kết nối.
    this.armLostTimer();
  }

  stop(): void {
    // Hủy hẹn giờ trước khi đóng. Hẹn giờ còn sống sẽ báo `lost` hoặc mở kết nối sau khi đã dừng.
    this.clearTimers();
    const source = this.eventSource;
    // Bỏ tham chiếu trước khi đóng. Sự kiện đến muộn từ kết nối cũ bị các hàm nhận bỏ qua.
    this.eventSource = null;
    source?.close();
  }

  private connect(): void {
    const source = this.createEventSource(this.options.url);
    this.eventSource = source;

    // Mỗi hàm nhận so sánh `source` với kết nối hiện tại. Kết nối cũ hoặc đã dừng không đổi trạng thái được.
    source.onopen = () => {
      if (this.eventSource === source) this.handleOpen();
    };
    source.onerror = () => {
      if (this.eventSource === source) this.handleError(source);
    };
    for (const eventName of this.options.eventNames) {
      source.addEventListener(eventName, (event) => {
        if (this.eventSource !== source) return;
        this.listener.onEvent(eventName, parseEventData(event.data));
      });
    }
  }

  private handleOpen(): void {
    // Luồng đã mở nên hủy cả hẹn giờ báo mất kết nối và hẹn giờ nối lại.
    this.clearTimers();
    // Lần mở thành công đặt lại thời gian chờ. Sự cố sau đó bắt đầu lại từ 5 giây.
    this.nextRetryDelayMs = INITIAL_RETRY_MS;
    this.setStatus('open');
    this.listener.onOpen();
  }

  private handleError(source: EventSourceLike): void {
    // Luồng đang mở bị rớt thì chuyển về `connecting`. Chữ `lost` chỉ hiện sau 5 giây không mở lại được.
    if (this.currentStatus === 'open') this.setStatus('connecting');
    this.armLostTimer();

    // `EventSource` còn ở trạng thái nối lại thì trình duyệt tự thử lại, nên không làm gì thêm.
    if (source.readyState !== EVENT_SOURCE_CLOSED) return;

    // Trình duyệt đã đóng hẳn, ví dụ API trả lỗi HTTP. Tự tạo kết nối mới sau một khoảng chờ.
    source.close();
    this.eventSource = null;
    this.scheduleRetry();
  }

  private scheduleRetry(): void {
    const delayMs = this.nextRetryDelayMs;
    // Nhân đôi thời gian chờ và chặn ở 30 giây. API đang lỗi không bị gọi dồn dập.
    this.nextRetryDelayMs = Math.min(delayMs * 2, MAX_RETRY_MS);
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      this.connect();
    }, delayMs);
  }

  private armLostTimer(): void {
    // Đã có hẹn giờ thì giữ nguyên. Nhiều lỗi liên tiếp không kéo dài thời điểm báo mất kết nối.
    if (this.lostTimer !== null) return;
    this.lostTimer = setTimeout(() => {
      this.lostTimer = null;
      this.setStatus('lost');
    }, LOST_AFTER_MS);
  }

  private clearTimers(): void {
    if (this.lostTimer !== null) clearTimeout(this.lostTimer);
    if (this.retryTimer !== null) clearTimeout(this.retryTimer);
    this.lostTimer = null;
    this.retryTimer = null;
  }

  private setStatus(status: StreamStatus): void {
    // Chỉ báo khi trạng thái thật sự đổi, để giao diện không vẽ lại vô ích.
    if (status === this.currentStatus) return;
    this.currentStatus = status;
    this.listener.onStatusChange(status);
  }
}
