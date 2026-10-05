import { afterEach, describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import type { PrismaClient } from '../../../../src/generated/prisma/client';
import { loadConfig } from '../../../../src/shared/config';
import { logger } from '../../../../src/shared/logger';
import {
  createAiGateway,
  FakeChatProvider,
  FakeEmbeddingProvider,
  ProviderError,
} from '../../../../src/modules/ai-gateway';
import type {
  AiGateway,
  CallContext,
  ChatChunk,
  ChatRequest,
  EmbeddingProvider,
} from '../../../../src/modules/ai-gateway';
import { Reliability } from '../../../../src/modules/ai-gateway/reliability';
import { persistUsageLog } from '../../../../src/modules/ai-gateway/usage-log';

type UsageEntry = {
  status: string;
  errorCode: string | null;
  costUsd: number;
  inputTokens: number;
  outputTokens: number;
};
function createStore() {
  const logs: UsageEntry[] = [];
  const cache = new Map<string, number[]>();
  const settings = {
    id: 'singleton',
    monthlyBudgetUsd: 30,
    chatDailyLimitUser: 50,
    chatDailyLimitAnon: 15,
  };
  const cacheWrite = vi.fn(async ({ create }: { create: { key: string; embedding: number[] } }) => {
    cache.set(create.key, create.embedding);
    return create;
  });
  const store = {
    aiSetting: { findUnique: async () => settings },
    aiUsageLog: {
      create: async ({ data }: { data: UsageEntry }) => {
        logs.push(data);
        return data;
      },
      aggregate: async () => ({
        _sum: { costUsd: logs.reduce((total, row) => total + row.costUsd, 0) },
      }),
    },
    aiEmbeddingCache: {
      findMany: async ({ where }: { where: { key: { in: string[] } } }) =>
        where.key.in.flatMap((key) => {
          const embedding = cache.get(key);
          return embedding ? [{ key, embedding }] : [];
        }),
      upsert: cacheWrite,
    },
  } as unknown as PrismaClient;
  return { store, logs, cache, settings, cacheWrite };
}
const context: CallContext = { feature: 'smart-search.parse-query', actorKey: 'user:regression' };
const request: ChatRequest = { messages: [{ role: 'user', content: 'Tìm áo' }] };
const usage = { inputTokens: 10, outputTokens: 5, cachedTokens: 0 };
async function callGateway(
  gateway: AiGateway,
  method: 'chat' | 'json' | 'stream' | 'embed',
  ctx = context,
): Promise<unknown> {
  if (method === 'chat') return gateway.chat(request, ctx);
  if (method === 'json') return gateway.chatJson(request, z.object({ ok: z.boolean() }), ctx);
  if (method === 'embed') return gateway.embed(['Tìm áo'], ctx);
  const chunks: ChatChunk[] = [];
  for await (const chunk of gateway.chatStream(request, ctx)) chunks.push(chunk);
  return chunks;
}
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('Gateway regression boundaries', () => {
  it.each(['chat', 'json', 'stream', 'embed'] as const)(
    'production thiếu khóa ghi một log outage cho %s',
    async (method) => {
      const { store, logs } = createStore();
      const config = loadConfig({
        NODE_ENV: 'production',
        DATABASE_URL: 'postgresql://test',
        GOOGLE_CLIENT_ID: 'real-client',
        GOOGLE_CLIENT_SECRET: 'real-secret',
      });
      const gateway = createAiGateway({ store, config });
      await expect(callGateway(gateway, method)).rejects.toMatchObject({ code: 'AI_UNAVAILABLE' });
      expect(logs).toMatchObject([
        { status: 'blocked', errorCode: 'AI_NOT_CONFIGURED', costUsd: 0 },
      ]);
    },
  );

  it('hạn mức chung cho chat, JSON và stream có hiệu lực ngay khi cấu hình thay đổi', async () => {
    const { store, logs, settings } = createStore();
    settings.chatDailyLimitAnon = 2;
    const provider = new FakeChatProvider();
    provider.enqueueResponse({ text: '{"ok":true}', usage });
    const gateway = createAiGateway({ store, chatProvider: provider });
    const chatContext: CallContext = {
      feature: 'shopping-assistant.chat',
      actorKey: 'anon:shared-limit',
    };
    expect(await callGateway(gateway, 'json', chatContext)).toEqual({ ok: true });
    await callGateway(gateway, 'stream', chatContext);
    await expect(callGateway(gateway, 'chat', chatContext)).rejects.toMatchObject({
      code: 'AI_BUDGET_EXCEEDED',
    });
    expect(logs.at(-1)).toMatchObject({ status: 'blocked', errorCode: 'RATE_LIMIT_EXCEEDED' });
    settings.chatDailyLimitAnon = 3;
    expect(await callGateway(gateway, 'chat', chatContext)).toMatchObject({
      text: 'Xin chào từ fake AI',
    });
    expect(provider.recordedRequests).toHaveLength(3);
  });

  it('tool history giữ JSON hợp lệ khi có số điện thoại, số dài và undefined', async () => {
    const { store, logs } = createStore();
    const provider = new FakeChatProvider();
    provider.enqueueResponse({
      text: '',
      usage,
      toolCalls: [
        {
          id: 'lookup1',
          name: 'lookup',
          input: { phone: 84912345678, orderId: 123456789012, price: 25990000 },
        },
      ],
    });
    provider.enqueueResponse({ text: 'Đã tìm thấy đơn', usage });
    let receivedOrderId: number | undefined;
    const gateway = createAiGateway({ store, chatProvider: provider });
    const result = await gateway.chat(
      {
        ...request,
        maxSteps: 2,
        tools: [
          {
            name: 'lookup',
            description: 'Tra cứu đơn',
            parameters: z.object({ phone: z.number(), orderId: z.number(), price: z.number() }),
            execute: async (input: unknown) => {
              const parsed = z.object({ orderId: z.number() }).parse(input);
              receivedOrderId = parsed.orderId;
              return { phone: 84912345678, account: 123456789012 };
            },
          },
        ],
      },
      context,
    );
    expect(result.text).toBe('Đã tìm thấy đơn');
    expect(receivedOrderId).toBe(123456789012);
    const history = provider.recordedRequests[1]?.messages;
    expect(history).toContainEqual({
      role: 'assistant',
      content: '',
      toolCalls: [
        {
          id: 'lookup1',
          name: 'lookup',
          input: { phone: '[đã ẩn]', orderId: '[đã ẩn]', price: 25990000 },
        },
      ],
    });
    expect(history).toContainEqual({
      role: 'tool',
      toolCallId: 'lookup1',
      toolName: 'lookup',
      content: '{"phone":"[đã ẩn]","account":"[đã ẩn]"}',
    });
    provider.enqueueResponse({ text: '{"ok":true}', usage });
    expect(
      await gateway.chatJson(
        {
          messages: [
            {
              role: 'assistant',
              content: '',
              toolCalls: [{ id: 'old', name: 'lookup', input: undefined }],
            },
          ],
        },
        z.object({ ok: z.boolean() }),
        context,
      ),
    ).toEqual({ ok: true });
    expect(logs.map((entry) => entry.status)).toEqual(['ok', 'ok', 'ok']);
  });

  it('lỗi HTTP provider có mã ổn định trong usage log, không chứa secret', async () => {
    const { store, logs } = createStore();
    const provider = new FakeChatProvider();
    provider.enqueueError(new ProviderError(401, false));
    const gateway = createAiGateway({ store, chatProvider: provider });
    await expect(gateway.chat(request, context)).rejects.toMatchObject({ code: 'AI_UNAVAILABLE' });
    expect(logs).toMatchObject([{ status: 'error', errorCode: 'PROVIDER_HTTP_401', costUsd: 0 }]);
  });

  it('single-flight chia sẻ vector đã lưu, giữ đúng thứ tự khi hai lời gọi chồng nhau', async () => {
    const { store, logs, cache } = createStore();
    const pending = Promise.withResolvers<{ vectors: number[][]; usage: typeof usage }>();
    const embed = vi.fn(() => pending.promise);
    const provider: EmbeddingProvider = { name: 'fake', model: 'fake-embedding', embed };
    const gateway = createAiGateway({ store, embeddingProvider: provider });
    const first = gateway.embed(['A', 'B'], context);
    await vi.waitFor(() => expect(embed).toHaveBeenCalledTimes(1));
    const second = gateway.embed(['B', 'A', 'B'], context);
    // Cho lời gọi thứ hai đăng ký chờ trước khi provider hoàn thành, không dùng độ trễ đồng hồ thực.
    const nextTurn = Promise.withResolvers<void>();
    setImmediate(nextTurn.resolve);
    await nextTurn.promise;
    pending.resolve({
      vectors: [
        [1, 2],
        [3, 4],
      ],
      usage,
    });
    expect(await first).toEqual([
      [1, 2],
      [3, 4],
    ]);
    expect(await second).toEqual([
      [3, 4],
      [1, 2],
      [3, 4],
    ]);
    expect(embed).toHaveBeenCalledTimes(1);
    expect(cache.size).toBe(2);
    expect(logs).toMatchObject([{ status: 'ok' }, { status: 'ok', costUsd: 0 }]);
  });

  it('lỗi ghi cache không trả vector chưa bền vững, lần gọi sau có thể phục hồi', async () => {
    const { store, logs, cacheWrite } = createStore();
    cacheWrite.mockRejectedValueOnce(new Error('DB cache unavailable'));
    const provider = new FakeEmbeddingProvider();
    provider.enqueueResponse({ vectors: [[1, 2]], usage });
    provider.enqueueResponse({ vectors: [[3, 4]], usage });
    const gateway = createAiGateway({ store, embeddingProvider: provider });
    await expect(gateway.embed(['A'], context)).rejects.toThrow();
    expect(await gateway.embed(['A'], context)).toEqual([[3, 4]]);
    expect(logs.map((entry) => entry.status)).toEqual(['error', 'ok']);
    expect(provider.recordedRequests).toHaveLength(2);
  });

  it('lỗi lưu usage log không lộ chuỗi kết nối qua logger hoặc exception', async () => {
    const secret = 'postgresql://user:secret-password@private-host/database';
    const { store } = createStore();
    store.aiUsageLog.create = vi.fn().mockRejectedValue(new Error(secret));
    const errorLogger = vi.spyOn(logger, 'error').mockImplementation(() => undefined);
    await expect(
      persistUsageLog(store, {
        feature: context.feature,
        provider: 'deepseek',
        model: 'deepseek-flash',
        latencyMs: 5,
        status: 'error',
      }),
    ).rejects.not.toThrow(secret);
    expect(JSON.stringify(errorLogger.mock.calls)).not.toContain(secret);
    expect(errorLogger).toHaveBeenCalledWith(
      expect.objectContaining({ errorCode: 'DB_PERSIST_ERROR' }),
      expect.any(String),
    );
  });

  it('stream idle sau token đầu hết hạn 15 giây, abort và không retry', async () => {
    vi.useFakeTimers();
    const reliability = new Reliability();
    const signals: AbortSignal[] = [];
    async function* stalled(signal: AbortSignal): AsyncIterable<ChatChunk> {
      signals.push(signal);
      yield { type: 'text', text: 'partial' };
      await Promise.withResolvers<never>().promise;
    }
    const iterator = reliability.stream('deepseek', stalled)[Symbol.asyncIterator]();
    expect(await iterator.next()).toMatchObject({ value: { type: 'text', text: 'partial' } });
    const rejected = expect(iterator.next()).rejects.toMatchObject({
      code: 'AI_UNAVAILABLE',
      reasonCode: 'PROVIDER_TIMEOUT',
    });
    await vi.advanceTimersByTimeAsync(15000);
    await rejected;
    expect(signals).toHaveLength(1);
    expect(signals[0]?.aborted).toBe(true);
  });

  it('probe hết hạn được giải phóng cả khi consumer ngừng đọc stream', async () => {
    vi.useFakeTimers();
    const reliability = new Reliability();
    for (let i = 0; i < 5; i += 1) {
      const lease = reliability.circuit.acquire('deepseek');
      reliability.circuit.failure('deepseek', lease);
    }
    await vi.advanceTimersByTimeAsync(60000);
    async function* stream(): AsyncIterable<ChatChunk> {
      yield { type: 'text', text: 'partial' };
    }
    const iterator = reliability.stream('deepseek', stream)[Symbol.asyncIterator]();
    await iterator.next();
    await vi.advanceTimersByTimeAsync(30000);
    expect(await reliability.run('deepseek', 30000, async () => 'recovered')).toBe('recovered');
    await iterator.return?.();
  });

  it('stream khỏe kéo dài hơn 30 giây không bị cắt hoặc làm mở circuit', async () => {
    vi.useFakeTimers();
    const reliability = new Reliability();
    for (let i = 0; i < 4; i += 1) {
      const lease = reliability.circuit.acquire('deepseek');
      reliability.circuit.failure('deepseek', lease);
    }
    async function* healthy(): AsyncIterable<ChatChunk> {
      for (let i = 0; i < 10; i += 1) {
        const tick = Promise.withResolvers<void>();
        setTimeout(tick.resolve, 5000);
        await tick.promise;
        yield { type: 'text', text: String(i) };
      }
      yield { type: 'finish', usage };
    }
    const collected = (async () => {
      let text = '';
      for await (const chunk of reliability.stream('deepseek', healthy)) {
        if (chunk.type === 'text') text += chunk.text;
      }
      return text;
    })();
    const complete = expect(collected).resolves.toBe('0123456789');
    // Matcher vẫn được await bên dưới, catch chỉ tránh báo rejection chưa xử lý khi đồng hồ giả đang chạy.
    complete.catch(() => undefined);
    await vi.advanceTimersByTimeAsync(50000);
    await complete;
    expect(await reliability.run('deepseek', 30000, async () => 'healthy')).toBe('healthy');
  });
  it('mạch mở ghi blocked và không gọi provider cho JSON hoặc stream', async () => {
    const { store, logs } = createStore();
    const reliability = new Reliability();
    const provider = new FakeChatProvider();
    for (let i = 0; i < 5; i += 1) {
      const lease = reliability.circuit.acquire(provider.name);
      reliability.circuit.failure(provider.name, lease);
    }
    const gateway = createAiGateway({ store, reliability, chatProvider: provider });
    await expect(callGateway(gateway, 'json')).rejects.toMatchObject({ code: 'AI_UNAVAILABLE' });
    await expect(callGateway(gateway, 'stream')).rejects.toMatchObject({ code: 'AI_UNAVAILABLE' });
    expect(provider.recordedRequests).toHaveLength(0);
    expect(logs).toMatchObject([
      { status: 'blocked', errorCode: 'CIRCUIT_OPEN', costUsd: 0 },
      { status: 'blocked', errorCode: 'CIRCUIT_OPEN', costUsd: 0 },
    ]);
  });

  it('sự cố provider hoàn lại lượt, không hoàn lại lượt đã thành công', async () => {
    const { store, settings } = createStore();
    settings.chatDailyLimitAnon = 2;
    const provider = new FakeChatProvider();
    const gateway = createAiGateway({ store, chatProvider: provider });
    const ctx: CallContext = { feature: 'shopping-assistant.chat', actorKey: 'anon:refund' };
    await gateway.chat(request, ctx);
    provider.enqueueError(new ProviderError(401, false));
    await expect(gateway.chat(request, ctx)).rejects.toMatchObject({ code: 'AI_UNAVAILABLE' });
    expect(await gateway.chat(request, ctx)).toMatchObject({ text: 'Xin chào từ fake AI' });
    await expect(gateway.chat(request, ctx)).rejects.toMatchObject({ code: 'AI_BUDGET_EXCEEDED' });
    expect(provider.recordedRequests).toHaveLength(3);
  });

  it('chặn ngân sách không lấy mất lượt sau khi admin bật lại AI', async () => {
    const { store, settings } = createStore();
    settings.chatDailyLimitAnon = 1;
    settings.monthlyBudgetUsd = 0;
    const gateway = createAiGateway({ store, chatProvider: new FakeChatProvider() });
    const ctx: CallContext = { feature: 'shopping-assistant.chat', actorKey: 'anon:budget-refund' };
    await expect(gateway.chat(request, ctx)).rejects.toMatchObject({ code: 'AI_BUDGET_EXCEEDED' });
    settings.monthlyBudgetUsd = 30;
    expect(await gateway.chat(request, ctx)).toMatchObject({ text: 'Xin chào từ fake AI' });
    await expect(gateway.chat(request, ctx)).rejects.toMatchObject({ code: 'AI_BUDGET_EXCEEDED' });
  });

  it('lỗi hôm trước không hoàn lại lượt đã dùng của ngày Việt Nam mới', async () => {
    const { store, settings } = createStore();
    settings.chatDailyLimitAnon = 1;
    let now = Date.UTC(2026, 9, 4, 16, 59, 59);
    const pending = Promise.withResolvers<never>();
    const provider = new FakeChatProvider();
    provider.enqueueHandler(() => pending.promise);
    const gateway = createAiGateway({ store, chatProvider: provider, now: () => now });
    const ctx: CallContext = {
      feature: 'shopping-assistant.chat',
      actorKey: 'anon:midnight-refund',
    };
    const oldCall = expect(gateway.chat(request, ctx)).rejects.toMatchObject({
      code: 'AI_UNAVAILABLE',
    });
    await vi.waitFor(() => expect(provider.recordedRequests).toHaveLength(1));
    now += 1000;
    await gateway.chat(request, ctx);
    pending.reject(new ProviderError(401, false));
    await oldCall;
    await expect(gateway.chat(request, ctx)).rejects.toMatchObject({ code: 'AI_BUDGET_EXCEEDED' });
    expect(provider.recordedRequests).toHaveLength(2);
  });

  it('lỗi đọc cache và chặn ngân sách embedding đều ghi đúng một log', async () => {
    const { store, logs, settings } = createStore();
    const provider = new FakeEmbeddingProvider();
    const readCache = vi
      .fn()
      .mockRejectedValueOnce(new Error('private database connection'))
      .mockResolvedValue([]);
    store.aiEmbeddingCache.findMany = readCache;
    const gateway = createAiGateway({ store, embeddingProvider: provider });
    await expect(gateway.embed(['A'], context)).rejects.toMatchObject({
      reasonCode: 'EMBEDDING_CACHE_READ_ERROR',
    });
    settings.monthlyBudgetUsd = 0;
    await expect(gateway.embed(['A'], context)).rejects.toMatchObject({
      code: 'AI_BUDGET_EXCEEDED',
    });
    expect(provider.recordedRequests).toHaveLength(0);
    expect(logs).toMatchObject([
      { status: 'error', errorCode: 'EMBEDDING_CACHE_READ_ERROR', costUsd: 0 },
      { status: 'blocked', errorCode: 'AI_BUDGET_EXCEEDED', costUsd: 0 },
    ]);
  });

  it('lỗi embedding không làm kẹt single-flight ở lần gọi tiếp theo', async () => {
    const { store, logs } = createStore();
    const provider = new FakeEmbeddingProvider();
    provider.enqueueError(new ProviderError(400, false));
    provider.enqueueResponse({ vectors: [[1, 2]], usage });
    const gateway = createAiGateway({ store, embeddingProvider: provider });
    await expect(gateway.embed(['A'], context)).rejects.toMatchObject({ code: 'AI_UNAVAILABLE' });
    expect(await gateway.embed(['A'], context)).toEqual([[1, 2]]);
    expect(logs).toMatchObject([
      { status: 'error', errorCode: 'PROVIDER_HTTP_400' },
      { status: 'ok' },
    ]);
  });

  it('stream đứt sau text ghi chi phí ước tính, stream đã có usage giữ số liệu provider', async () => {
    const { store, logs } = createStore();
    const provider = new FakeChatProvider({ model: 'deepseek-flash' });
    provider.stream = async function* (): AsyncIterable<ChatChunk> {
      yield { type: 'text', text: 'hello' };
      throw new ProviderError(500, true);
    };
    const gateway = createAiGateway({ store, chatProvider: provider });
    await expect(callGateway(gateway, 'stream')).rejects.toMatchObject({ code: 'AI_UNAVAILABLE' });
    expect(logs[0]).toMatchObject({
      status: 'error',
      errorCode: 'STREAM_PARTIAL_ESTIMATED_PROVIDER_HTTP_500',
      outputTokens: 5,
    });
    expect(logs[0]?.costUsd).toBeGreaterThan(0);
    provider.stream = async function* (): AsyncIterable<ChatChunk> {
      yield { type: 'text', text: 'hello' };
      yield { type: 'finish', usage };
      throw new ProviderError(500, true);
    };
    await expect(callGateway(gateway, 'stream')).rejects.toMatchObject({ code: 'AI_UNAVAILABLE' });
    expect(logs[1]).toMatchObject({
      inputTokens: 10,
      outputTokens: 5,
      errorCode: 'PROVIDER_HTTP_500',
    });
  });

  it('hủy stream sau text vẫn ghi một log có chi phí ước tính', async () => {
    const { store, logs } = createStore();
    const gateway = createAiGateway({
      store,
      chatProvider: new FakeChatProvider({ model: 'deepseek-flash' }),
    });
    const iterator = gateway.chatStream(request, context)[Symbol.asyncIterator]();
    await iterator.next();
    await iterator.return?.();
    expect(logs).toMatchObject([
      { status: 'error', errorCode: 'STREAM_CANCELLED_PARTIAL_ESTIMATED' },
    ]);
    expect(logs[0]?.costUsd).toBeGreaterThan(0);
  });
});
