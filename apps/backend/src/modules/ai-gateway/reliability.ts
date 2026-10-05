import { CircuitBreaker, type CircuitLease } from './circuit-breaker';
import { AiUnavailableError, getAiErrorCode, ProviderError } from './errors';
import type { ChatChunk } from './types';

type ReliabilityOptions = { now?: () => number; random?: () => number };

function delay(milliseconds: number): Promise<void> {
  const { promise, resolve } = Promise.withResolvers<void>();
  setTimeout(resolve, milliseconds);
  return promise;
}

async function withDeadline<T>(
  operation: Promise<T>,
  controller: AbortController,
  milliseconds: number,
): Promise<T> {
  const { promise, reject } = Promise.withResolvers<T>();
  // Giới hạn bằng race để provider không tuân thủ AbortSignal vẫn không treo gateway.
  const timer = setTimeout(() => {
    controller.abort();
    reject(new ProviderError(undefined, true, 'PROVIDER_TIMEOUT'));
  }, milliseconds);
  try {
    return await Promise.race([operation, promise]);
  } finally {
    // Xóa timer cả khi lỗi để không abort nhầm một lời gọi đã hoàn thành.
    clearTimeout(timer);
  }
}

export class Reliability {
  readonly circuit: CircuitBreaker;
  private readonly random: () => number;

  constructor(options: ReliabilityOptions = {}) {
    this.circuit = new CircuitBreaker(options.now);
    this.random = options.random ?? Math.random;
  }

  assertAvailable(provider: string): void {
    // Gateway kiểm tra sớm trước truy vấn DB để mạch mở trả lỗi ngay.
    this.circuit.assertAvailable(provider);
  }

  private retryable(error: unknown): boolean {
    return error instanceof ProviderError && error.retryable;
  }

  private recordFailure(provider: string, lease: CircuitLease, error: unknown): void {
    // Lỗi 4xx do request không phải sự cố provider, không làm tăng bộ đếm mở mạch.
    if (this.retryable(error)) this.circuit.failure(provider, lease);
    else this.circuit.release(provider, lease);
  }

  async run<T>(
    provider: string,
    timeoutMs: number,
    operation: (signal: AbortSignal) => Promise<T>,
  ): Promise<T> {
    for (let attempt = 0; attempt < 3; attempt += 1) {
      // Acquire nằm ngoài catch để mạch mở không bị retry hoặc chờ backoff.
      const lease = this.circuit.acquire(provider);
      const controller = new AbortController();
      try {
        const result = await withDeadline(
          Promise.resolve().then(() => operation(controller.signal)),
          controller,
          timeoutMs,
        );
        this.circuit.success(provider, lease);
        return result;
      } catch (error) {
        controller.abort();
        this.recordFailure(provider, lease, error);
        // Probe chỉ có một request; lỗi thường chỉ được thử lại tối đa hai lần.
        if (!this.retryable(error) || lease.probe || attempt === 2)
          throw new AiUnavailableError(getAiErrorCode(error));
        try {
          this.circuit.assertAvailable(provider);
        } catch {
          throw new AiUnavailableError(getAiErrorCode(error));
        }
        await delay((attempt === 0 ? 500 : 1500) + Math.floor(this.random() * 100));
      }
    }
    throw new AiUnavailableError();
  }

  async *stream(
    provider: string,
    operation: (signal: AbortSignal) => AsyncIterable<ChatChunk>,
  ): AsyncIterable<ChatChunk> {
    for (let attempt = 0; attempt < 3; attempt += 1) {
      const lease = this.circuit.acquire(provider);
      const controller = new AbortController();
      let iterator: AsyncIterator<ChatChunk> | undefined;
      let emitted = false;
      let finished = false;
      let consumerTimer: NodeJS.Timeout | undefined;
      try {
        iterator = operation(controller.signal)[Symbol.asyncIterator]();
        // Đồng hồ bắt đầu lúc mở stream, không đặt lại khi có các chunk text rỗng.
        const deadline = Date.now() + 10000;
        while (true) {
          // Chỉ đếm thời gian provider đang được chờ, không tính thời gian client giữ một chunk chưa đọc tiếp.
          const timeoutMs = emitted ? 15000 : deadline - Date.now();
          if (timeoutMs <= 0) throw new ProviderError(undefined, true, 'PROVIDER_TIMEOUT');
          const next = await withDeadline(iterator.next(), controller, timeoutMs);
          // Stream đóng mà thiếu usage không được coi là thành công hoặc ghi chi phí bằng không.
          if (next.done) {
            if (!finished) throw new ProviderError(undefined, true, 'PROVIDER_INCOMPLETE_STREAM');
            this.circuit.success(provider, lease);
            return;
          }
          const chunk = next.value;
          if (chunk.type === 'text' && !chunk.text) continue;
          emitted = true;
          if (chunk.type === 'finish') finished = true;
          // Client im lặng phải nhả probe và hủy tài nguyên, nhưng không được tính như sự cố provider.
          consumerTimer = setTimeout(() => {
            controller.abort();
            this.circuit.release(provider, lease);
          }, 30000);
          yield chunk;
          clearTimeout(consumerTimer);
          consumerTimer = undefined;
          if (controller.signal.aborted)
            throw new ProviderError(undefined, false, 'STREAM_CONSUMER_TIMEOUT');
        }
      } catch (error) {
        this.recordFailure(provider, lease, error);
        // Sau text đầu tiên, retry sẽ lặp nội dung khách đã nhận nên phải dừng ngay.
        if (emitted || !this.retryable(error) || lease.probe || attempt === 2)
          throw new AiUnavailableError(getAiErrorCode(error));
        try {
          this.circuit.assertAvailable(provider);
        } catch {
          throw new AiUnavailableError(getAiErrorCode(error));
        }
      } finally {
        clearTimeout(consumerTimer);
        controller.abort();
        // Không await return vì iterator bị treo có thể bỏ qua AbortSignal.
        if (iterator?.return) void iterator.return().catch(() => undefined);
        this.circuit.release(provider, lease);
      }
      await delay((attempt === 0 ? 500 : 1500) + Math.floor(this.random() * 100));
    }
  }
}
