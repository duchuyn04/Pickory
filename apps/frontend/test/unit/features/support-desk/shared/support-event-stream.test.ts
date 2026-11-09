import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../../../../src/shared/api';
import {
  SupportEventStream,
  type StreamStatus,
} from '../../../../../src/features/support-desk/shared/support-event-stream';
// Tách deferred promise để điều khiển thời điểm phản hồi của request đầu.
interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (reason?: unknown) => void;
}

function createDeferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}
// Tải lại module qua vi.resetModules để kịch bản có trạng thái request đầu độc lập.
async function loadFreshStreamContext() {
  vi.resetModules();
  const streamModule =
    await import('../../../../../src/features/support-desk/shared/support-event-stream');
  const apiModule = await import('../../../../../src/shared/api');
  return {
    SupportEventStream: streamModule.SupportEventStream,
    apiRequest: apiModule.apiRequest,
  };
}

function startStreamWith(StreamClass: typeof SupportEventStream) {
  const recorder: Recorder = { statuses: [], openCount: 0, events: [] };
  const stream = new StreamClass(
    {
      url: '/api/admin/support/events',
      eventNames: ['presence.changed', 'session.waiting'],
      createEventSource: (url) => new FakeEventSource(url),
    },
    {
      onStatusChange: (status) => recorder.statuses.push(status),
      onOpen: () => {
        recorder.openCount += 1;
      },
      onEvent: (name, data) => recorder.events.push({ name, data }),
    },
  );
  stream.start();
  return { stream, recorder };
}

import { FakeEventSource } from './fake-event-source';

interface Recorder {
  statuses: StreamStatus[];
  openCount: number;
  events: { name: string; data: unknown }[];
}

function startStream() {
  const recorder: Recorder = { statuses: [], openCount: 0, events: [] };
  const stream = new SupportEventStream(
    {
      url: '/api/admin/support/events',
      eventNames: ['presence.changed', 'session.waiting'],
      createEventSource: (url) => new FakeEventSource(url),
    },
    {
      onStatusChange: (status) => recorder.statuses.push(status),
      onOpen: () => {
        recorder.openCount += 1;
      },
      onEvent: (name, data) => recorder.events.push({ name, data }),
    },
  );
  stream.start();
  return { stream, recorder };
}

function latestSource(): FakeEventSource {
  const source = FakeEventSource.instances.at(-1);
  if (!source) throw new Error('Chưa có EventSource nào.');
  return source;
}

beforeEach(async () => {
  FakeEventSource.instances = [];
  vi.useFakeTimers();
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify({ data: { ok: true } }), { status: 200 })),
  );
  // Giải phóng bước chờ request đầu để các test chức năng của luồng mở kết nối.
  await apiRequest('/api/init');
  FakeEventSource.instances = [];
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('SupportEventStream: trạng thái kết nối', () => {
  it('mở đúng một kết nối tới URL của luồng và bắt đầu ở connecting', () => {
    const { stream } = startStream();

    expect(FakeEventSource.instances).toHaveLength(1);
    expect(latestSource().url).toBe('/api/admin/support/events');
    expect(stream.status).toBe('connecting');
  });

  it('chuyển sang open khi luồng mở và báo onOpen', () => {
    const { stream, recorder } = startStream();

    latestSource().open();

    expect(stream.status).toBe('open');
    expect(recorder.statuses).toEqual(['open']);
    expect(recorder.openCount).toBe(1);
  });

  it('chưa hiện lost ở giây thứ 4 và hiện lost ở giây thứ 5 khi luồng chưa mở', () => {
    const { stream } = startStream();

    vi.advanceTimersByTime(4999);
    expect(stream.status).toBe('connecting');

    vi.advanceTimersByTime(1);
    expect(stream.status).toBe('lost');
  });

  it('không hiện lost khi trình duyệt nối lại trong 5 giây, như lúc luồng đóng đều đặn mỗi 5 phút', () => {
    const { stream, recorder } = startStream();
    const source = latestSource();
    source.open();

    source.dropAndReconnectByBrowser();
    expect(stream.status).toBe('connecting');
    vi.advanceTimersByTime(3000);
    source.open();
    vi.advanceTimersByTime(60_000);

    expect(stream.status).toBe('open');
    expect(recorder.statuses).not.toContain('lost');
    expect(recorder.openCount).toBe(2);
  });

  it('hiện lost sau 5 giây tính từ lúc luồng đang mở bị rớt', () => {
    const { stream } = startStream();
    const source = latestSource();
    source.open();

    source.dropAndReconnectByBrowser();
    vi.advanceTimersByTime(4999);
    expect(stream.status).toBe('connecting');

    vi.advanceTimersByTime(1);
    expect(stream.status).toBe('lost');
  });

  it('về open và báo onOpen khi luồng mở lại sau lost', () => {
    const { stream, recorder } = startStream();
    const source = latestSource();
    vi.advanceTimersByTime(5000);
    expect(stream.status).toBe('lost');

    source.open();

    expect(stream.status).toBe('open');
    expect(recorder.openCount).toBe(1);
  });
});

describe('SupportEventStream: tự nối lại khi luồng đóng hẳn', () => {
  it('tạo kết nối mới sau 5 giây khi EventSource ở CLOSED', () => {
    startStream();
    const first = latestSource();

    first.failPermanently();
    expect(FakeEventSource.instances).toHaveLength(1);
    vi.advanceTimersByTime(4999);
    expect(FakeEventSource.instances).toHaveLength(1);

    vi.advanceTimersByTime(1);
    expect(FakeEventSource.instances).toHaveLength(2);
    expect(first.closeCalls).toBeGreaterThanOrEqual(1);
  });

  it('giãn thời gian giữa các lần thử 5, 10, 20 rồi giữ ở 30 giây', () => {
    startStream();
    const expectedDelays = [5000, 10_000, 20_000, 30_000, 30_000];

    for (const [attempt, delay] of expectedDelays.entries()) {
      latestSource().failPermanently();
      const instancesBefore = FakeEventSource.instances.length;

      vi.advanceTimersByTime(delay - 1);
      expect(FakeEventSource.instances, `lần thử ${attempt + 1} chưa tới hạn`).toHaveLength(
        instancesBefore,
      );
      vi.advanceTimersByTime(1);
      expect(FakeEventSource.instances, `lần thử ${attempt + 1} tới hạn`).toHaveLength(
        instancesBefore + 1,
      );
    }
  });

  it('đặt lại thời gian chờ về 5 giây sau khi luồng mở được', () => {
    startStream();
    latestSource().failPermanently();
    vi.advanceTimersByTime(5000);
    latestSource().failPermanently();
    vi.advanceTimersByTime(10_000);

    latestSource().open();
    latestSource().failPermanently();
    const instancesBefore = FakeEventSource.instances.length;
    vi.advanceTimersByTime(5000);

    expect(FakeEventSource.instances).toHaveLength(instancesBefore + 1);
  });

  it('giữ lost trong lúc nối lại, không nháy về connecting', () => {
    const { stream, recorder } = startStream();
    latestSource().failPermanently();
    vi.advanceTimersByTime(5000);
    expect(stream.status).toBe('lost');

    vi.advanceTimersByTime(5000);

    expect(stream.status).toBe('lost');
    expect(recorder.statuses.filter((status) => status === 'connecting')).toHaveLength(0);
  });

  it('báo onOpen khi kết nối mới mở được, để client đọc lại dữ liệu đã lỡ', () => {
    const { recorder } = startStream();
    latestSource().failPermanently();
    vi.advanceTimersByTime(5000);

    latestSource().open();

    expect(recorder.openCount).toBe(1);
  });
});

describe('SupportEventStream: sự kiện', () => {
  it('chuyển sự kiện theo tên, kèm dữ liệu JSON đã giải mã', () => {
    const { recorder } = startStream();
    latestSource().open();

    latestSource().emit('presence.changed', { staffId: 'staff-1', status: 'away' });
    latestSource().emit('session.waiting', { sessionId: 'session-9' });

    expect(recorder.events).toEqual([
      { name: 'presence.changed', data: { staffId: 'staff-1', status: 'away' } },
      { name: 'session.waiting', data: { sessionId: 'session-9' } },
    ]);
  });

  it('vẫn chuyển sự kiện với dữ liệu null khi JSON hỏng', () => {
    const { recorder } = startStream();
    latestSource().open();

    latestSource().emitRaw('session.waiting', 'không phải JSON');

    expect(recorder.events).toEqual([{ name: 'session.waiting', data: null }]);
  });
});

describe('SupportEventStream: dừng', () => {
  it('đóng EventSource và không tạo kết nối mới sau stop', () => {
    const { stream } = startStream();
    const source = latestSource();
    source.failPermanently();

    stream.stop();
    vi.advanceTimersByTime(60_000);

    expect(FakeEventSource.instances).toHaveLength(1);
    expect(source.closeCalls).toBeGreaterThanOrEqual(1);
  });

  it('không đổi trạng thái sang lost sau stop', () => {
    const { stream, recorder } = startStream();

    stream.stop();
    vi.advanceTimersByTime(60_000);

    expect(recorder.statuses).not.toContain('lost');
  });

  it('bỏ qua sự kiện đến muộn từ kết nối đã dừng', () => {
    const { stream, recorder } = startStream();
    const source = latestSource();
    source.open();

    stream.stop();
    source.emit('session.waiting', { sessionId: 'x' });

    expect(recorder.events).toEqual([]);
  });
});

describe('SupportEventStream: chờ request API đầu tiên', () => {
  it('chờ request API đầu tiên hoàn tất mới tạo kết nối EventSource', async () => {
    const { SupportEventStream: FreshStream, apiRequest: freshApiRequest } =
      await loadFreshStreamContext();
    const firstDeferred = createDeferred<Response>();
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => firstDeferred.promise),
    );

    const firstApiCall = freshApiRequest('/api/init');

    const { stream } = startStreamWith(FreshStream);

    // Chưa tạo kết nối EventSource trong lúc chờ request đầu.
    expect(FakeEventSource.instances).toHaveLength(0);

    // Giải phóng request đầu.
    firstDeferred.resolve(new Response(JSON.stringify({ data: { ok: true } }), { status: 200 }));
    await firstApiCall;

    // Chờ microtask để stream tiến hành mở kết nối.
    await Promise.resolve();

    expect(FakeEventSource.instances).toHaveLength(1);
    expect(latestSource().url).toBe('/api/admin/support/events');
    expect(stream.status).toBe('connecting');
  });

  it('dừng luồng trong lúc chờ request đầu thì không tạo kết nối sau khi request xong', async () => {
    const { SupportEventStream: FreshStream, apiRequest: freshApiRequest } =
      await loadFreshStreamContext();
    const firstDeferred = createDeferred<Response>();
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => firstDeferred.promise),
    );

    const firstApiCall = freshApiRequest('/api/init');

    const { stream } = startStreamWith(FreshStream);
    expect(FakeEventSource.instances).toHaveLength(0);

    // Dừng luồng ngay trong lúc chờ request đầu.
    stream.stop();

    // Giải phóng request đầu.
    firstDeferred.resolve(new Response(JSON.stringify({ data: { ok: true } }), { status: 200 }));
    await firstApiCall;
    await Promise.resolve();

    // Kết nối không được tạo sau khi luồng đã dừng.
    expect(FakeEventSource.instances).toHaveLength(0);
  });

  it('luồng bắt đầu trước khi request API được gọi thì chờ request đó hoàn tất mới mở kết nối', async () => {
    const { SupportEventStream: FreshStream, apiRequest: freshApiRequest } =
      await loadFreshStreamContext();
    const firstDeferred = createDeferred<Response>();
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => firstDeferred.promise),
    );

    // Bắt đầu luồng trước khi có bất kỳ request API nào được gọi.
    const { stream } = startStreamWith(FreshStream);

    // Chưa tạo kết nối EventSource khi chưa có request API nào chạy.
    expect(FakeEventSource.instances).toHaveLength(0);

    // Gọi request API đầu tiên với deferred promise chưa mở.
    const firstApiCall = freshApiRequest('/api/init');
    await Promise.resolve();

    // Trong lúc request đầu đang chạy, luồng vẫn chưa được tạo.
    expect(FakeEventSource.instances).toHaveLength(0);

    // Giải phóng request đầu tiên.
    firstDeferred.resolve(new Response(JSON.stringify({ data: { ok: true } }), { status: 200 }));
    await firstApiCall;

    // Chờ microtask để luồng tiến hành mở kết nối.
    await Promise.resolve();

    // Khi request đầu hoàn tất, kết nối EventSource được tạo.
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(latestSource().url).toBe('/api/admin/support/events');
    expect(stream.status).toBe('connecting');
  });
});
