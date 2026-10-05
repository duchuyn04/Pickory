import { afterEach, describe, expect, it, vi } from 'vitest';
import { Reliability } from '../../../../src/modules/ai-gateway/reliability';
import { AiUnavailableError, ProviderError } from '../../../../src/modules/ai-gateway/errors';
import type { ChatChunk } from '../../../../src/modules/ai-gateway/types';

afterEach(() => vi.useRealTimers());

describe('Reliability', () => {
  it('chỉ retry lỗi tạm thời, tối đa ba lần với backoff', async () => {
    vi.useFakeTimers();
    const reliability = new Reliability({ random: () => 0 });
    let attempts = 0;
    const pending = reliability.run('deepseek', 30000, async () => {
      attempts += 1;
      if (attempts < 3) throw new ProviderError(429, true);
      return 'ok';
    });
    await vi.advanceTimersByTimeAsync(499);
    expect(attempts).toBe(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(attempts).toBe(2);
    await vi.advanceTimersByTimeAsync(1500);
    expect(await pending).toBe('ok');
    expect(attempts).toBe(3);
    const rejected = vi.fn(async () => {
      throw new ProviderError(400, false);
    });
    await expect(reliability.run('deepseek', 30000, rejected)).rejects.toBeInstanceOf(
      AiUnavailableError,
    );
    expect(rejected).toHaveBeenCalledTimes(1);
  });
  it('năm lỗi mở mạch, không gọi ra ngoài, sau 60 giây chỉ có một probe', async () => {
    vi.useFakeTimers();
    const reliability = new Reliability({ random: () => 0 });
    const failure = vi.fn(async () => {
      throw new ProviderError(503, true);
    });
    const first = expect(reliability.run('deepseek', 30000, failure)).rejects.toBeInstanceOf(
      AiUnavailableError,
    );
    await vi.runAllTimersAsync();
    await first;
    const second = expect(reliability.run('deepseek', 30000, failure)).rejects.toBeInstanceOf(
      AiUnavailableError,
    );
    await vi.advanceTimersByTimeAsync(500);
    await second;
    expect(failure).toHaveBeenCalledTimes(5);
    const blocked = vi.fn(async () => 'unexpected');
    await expect(reliability.run('deepseek', 30000, blocked)).rejects.toBeInstanceOf(
      AiUnavailableError,
    );
    expect(blocked).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(60000);
    const probeResult = Promise.withResolvers<string>();
    const probe = reliability.run('deepseek', 30000, () => probeResult.promise);
    await expect(reliability.run('deepseek', 30000, blocked)).rejects.toBeInstanceOf(
      AiUnavailableError,
    );
    probeResult.resolve('recovered');
    expect(await probe).toBe('recovered');
    expect(await reliability.run('deepseek', 30000, async () => 'closed')).toBe('closed');
  });
  it('timeout hủy request và trả lỗi an toàn dù provider không tự kết thúc', async () => {
    vi.useFakeTimers();
    const reliability = new Reliability({ random: () => 0 });
    const signals: AbortSignal[] = [];
    const pending = expect(
      reliability.run('openai', 15000, (signal) => {
        signals.push(signal);
        return Promise.withResolvers<never>().promise;
      }),
    ).rejects.toBeInstanceOf(AiUnavailableError);
    await vi.runAllTimersAsync();
    await pending;
    expect(signals).toHaveLength(3);
    expect(signals.every((signal) => signal.aborted)).toBe(true);
  });
  it('stream đã gửi text thì không retry khi lỗi giữa chừng', async () => {
    const reliability = new Reliability();
    let attempts = 0;
    async function* operation(): AsyncIterable<ChatChunk> {
      attempts += 1;
      yield { type: 'text', text: 'hello' };
      throw new ProviderError(500, true);
    }
    const iterator = reliability.stream('deepseek', operation)[Symbol.asyncIterator]();
    expect(await iterator.next()).toEqual({ value: { type: 'text', text: 'hello' }, done: false });
    await expect(iterator.next()).rejects.toBeInstanceOf(AiUnavailableError);
    expect(attempts).toBe(1);
  });
  it('stream phải có token đầu trong mười giây', async () => {
    vi.useFakeTimers();
    const reliability = new Reliability({ random: () => 0 });
    const signals: AbortSignal[] = [];
    async function* operation(signal: AbortSignal): AsyncIterable<ChatChunk> {
      signals.push(signal);
      await Promise.withResolvers<never>().promise;
      yield { type: 'text', text: 'late' };
    }
    const rejected = expect(
      reliability.stream('deepseek', operation)[Symbol.asyncIterator]().next(),
    ).rejects.toBeInstanceOf(AiUnavailableError);
    await vi.runAllTimersAsync();
    await rejected;
    expect(signals).toHaveLength(3);
    expect(signals.every((signal) => signal.aborted)).toBe(true);
  });
});
