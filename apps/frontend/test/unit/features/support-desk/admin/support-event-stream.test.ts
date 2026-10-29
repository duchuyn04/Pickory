import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  SupportEventStream,
  type StreamStatus,
} from '../../../../../src/features/support-desk/admin/support-event-stream';
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

beforeEach(() => {
  FakeEventSource.instances = [];
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
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
