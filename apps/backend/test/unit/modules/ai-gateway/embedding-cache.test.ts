import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import type { PrismaClient } from '../../../../src/generated/prisma/client';
import { executeEmbedWithCache } from '../../../../src/modules/ai-gateway/embedding-cache';
import { FakeEmbeddingProvider } from '../../../../src/modules/ai-gateway/fake-provider';
import { Reliability } from '../../../../src/modules/ai-gateway/reliability';
import type { AiSettings, CallContext } from '../../../../src/modules/ai-gateway/types';

describe('Bộ nhớ đệm embedding và tối ưu hóa chi phí (embedding-cache)', () => {
  const dummyContext: CallContext = {
    feature: 'smart-search.index',
    actorKey: 'user:staff_search_admin',
  };

  const dummySettings: AiSettings = {
    monthlyBudgetUsd: 100,
    chatDailyLimitUser: 50,
    chatDailyLimitAnon: 15,
  };

  const dummyAiSettingMock = {
    findUnique: async () => ({
      id: 'singleton',
      monthlyBudgetUsd: dummySettings.monthlyBudgetUsd,
      chatDailyLimitUser: dummySettings.chatDailyLimitUser,
      chatDailyLimitAnon: dummySettings.chatDailyLimitAnon,
    }),
  };

  it('xử lý mảng rỗng: trả về mảng rỗng và ghi đúng 1 dòng log cost = 0', async () => {
    const fakeProvider = new FakeEmbeddingProvider();
    const loggedEntries: Array<{ status: string; costUsd: number }> = [];

    const mockStore = {
      aiUsageLog: {
        create: async ({ data }: { data: { status: string; costUsd: number } }) => {
          loggedEntries.push(data);
          return {};
        },
      },
      aiSetting: dummyAiSettingMock,
    } as unknown as PrismaClient;

    const result = await executeEmbedWithCache({
      provider: fakeProvider,
      reliability: new Reliability(),
      store: mockStore,
      settings: dummySettings,
      texts: [],
      ctx: dummyContext,
    });

    expect(result).toEqual([]);
    // Bắt buộc ghi đúng 1 dòng nhật ký theo hợp đồng every-call-1-log.
    expect(loggedEntries).toHaveLength(1);
    expect(loggedEntries[0]?.status).toBe('ok');
    expect(loggedEntries[0]?.costUsd).toBe(0);
    expect(fakeProvider.recordedRequests).toHaveLength(0);
  });

  it('tái sử dụng cache khi hai văn bản chứa PII khác nhau nhưng chuẩn hóa thành cùng nội dung đã ẩn', async () => {
    const fakeProvider = new FakeEmbeddingProvider();
    const cacheStorage = new Map<string, number[]>();

    const mockStore = {
      aiEmbeddingCache: {
        findMany: async ({ where }: { where: { key: { in: string[] } } }) => {
          const results: Array<{ key: string; embedding: number[] }> = [];
          for (const k of where.key.in) {
            const hit = cacheStorage.get(k);
            if (hit) results.push({ key: k, embedding: hit });
          }
          return results;
        },
        upsert: async ({
          where,
          create,
        }: {
          where: { key: string };
          create: { key: string; embedding: number[] };
        }) => {
          cacheStorage.set(where.key, create.embedding);
          return create;
        },
      },
      aiUsageLog: {
        create: async () => ({}),
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: dummyAiSettingMock,
    } as unknown as PrismaClient;

    const reliability = new Reliability();

    // Chuỗi 1 chứa số điện thoại thứ nhất.
    const text1 = 'Tư vấn mua sắm số 0912 345 678';
    // Chuỗi 2 chứa số điện thoại khác nhưng cùng mẫu, sau khi ẩn đều thành 'Tư vấn mua sắm số [đã ẩn]'.
    const text2 = 'Tư vấn mua sắm số 0987 654 321';

    // Gọi lần 1 với chuỗi thứ nhất: provider phải được gọi.
    const result1 = await executeEmbedWithCache({
      provider: fakeProvider,
      reliability,
      store: mockStore,
      settings: dummySettings,
      texts: [text1],
      ctx: dummyContext,
    });

    expect(fakeProvider.recordedRequests).toHaveLength(1);
    expect(fakeProvider.recordedRequests[0]).toEqual(['Tư vấn mua sắm số [đã ẩn]']);

    // Khóa lưu trong cache phải khớp chính xác với SHA-256 tính toán độc lập của model + \0 + văn bản đã ẩn.
    const expectedStoredKey = createHash('sha256')
      .update('fake-embedding\0Tư vấn mua sắm số [đã ẩn]')
      .digest('hex');
    expect(cacheStorage.has(expectedStoredKey)).toBe(true);

    fakeProvider.clearRecorded();

    // Gọi lần 2 với chuỗi thứ hai: trúng cache vì nội dung đã ẩn trùng nhau, provider không bị gọi lại.
    const result2 = await executeEmbedWithCache({
      provider: fakeProvider,
      reliability,
      store: mockStore,
      settings: dummySettings,
      texts: [text2],
      ctx: dummyContext,
    });

    expect(fakeProvider.recordedRequests).toHaveLength(0);
    expect(result2).toEqual(result1);
  });

  it('gọi embed 2 lần cùng text chỉ phát sinh 1 request tới provider và giữ đúng thứ tự mảng', async () => {
    const fakeProvider = new FakeEmbeddingProvider();

    const cacheStorage = new Map<string, number[]>();
    const loggedEntries: Array<{ costUsd: number; status: string }> = [];

    const mockStore = {
      aiEmbeddingCache: {
        findMany: async ({ where }: { where: { key: { in: string[] } } }) => {
          const results: Array<{ key: string; embedding: number[] }> = [];
          for (const k of where.key.in) {
            const hit = cacheStorage.get(k);
            if (hit) results.push({ key: k, embedding: hit });
          }
          return results;
        },
        upsert: async ({
          where,
          create,
        }: {
          where: { key: string };
          create: { key: string; embedding: number[] };
        }) => {
          cacheStorage.set(where.key, create.embedding);
          return create;
        },
      },
      aiUsageLog: {
        create: async ({ data }: { data: { costUsd: number; status: string } }) => {
          loggedEntries.push(data);
          return {};
        },
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: dummyAiSettingMock,
    } as unknown as PrismaClient;

    const reliability = new Reliability();

    // Lần gọi 1: text chưa có trong cache -> phải gọi provider.
    const result1 = await executeEmbedWithCache({
      provider: fakeProvider,
      reliability,
      store: mockStore,
      settings: dummySettings,
      texts: ['sản phẩm A', 'sản phẩm B'],
      ctx: dummyContext,
    });

    expect(result1).toHaveLength(2);
    const firstVector1 = result1[0];
    expect(firstVector1).toBeDefined();
    if (!firstVector1) throw new Error('Thiếu vector thứ nhất');
    expect(firstVector1).toHaveLength(1536);

    expect(fakeProvider.recordedRequests).toHaveLength(1);
    expect(loggedEntries).toHaveLength(1);

    // Lần gọi 2: cùng danh sách text -> trúng cache 100%, không phát sinh thêm request nào tới provider.
    const result2 = await executeEmbedWithCache({
      provider: fakeProvider,
      reliability,
      store: mockStore,
      settings: dummySettings,
      texts: ['sản phẩm A', 'sản phẩm B'],
      ctx: dummyContext,
    });

    expect(result2).toHaveLength(2);
    expect(fakeProvider.recordedRequests).toHaveLength(1); // Vẫn là 1 request.
    expect(loggedEntries).toHaveLength(2); // Có thêm 1 dòng log nhưng costUsd = 0.
    expect(loggedEntries[1]?.costUsd).toBe(0);

    // Dữ liệu vector trả về của 2 lần gọi phải hoàn toàn trùng khớp.
    const firstVector2 = result2[0];
    const secondVector2 = result2[1];
    expect(firstVector2).toBeDefined();
    expect(secondVector2).toBeDefined();
    if (!firstVector2 || !secondVector2) throw new Error('Thiếu vector lần hai');
    expect(firstVector2).toEqual(firstVector1);
    expect(secondVector2).toEqual(result1[1]);
  });

  it('chỉ gửi text mới khi mảng trộn lẫn giữa text cũ và text mới, giữ nguyên đúng thứ tự kết quả', async () => {
    const fakeProvider = new FakeEmbeddingProvider();
    const cacheStorage = new Map<string, number[]>();

    const mockStore = {
      aiEmbeddingCache: {
        findMany: async ({ where }: { where: { key: { in: string[] } } }) => {
          const results: Array<{ key: string; embedding: number[] }> = [];
          for (const k of where.key.in) {
            const hit = cacheStorage.get(k);
            if (hit) results.push({ key: k, embedding: hit });
          }
          return results;
        },
        upsert: async ({
          where,
          create,
        }: {
          where: { key: string };
          create: { key: string; embedding: number[] };
        }) => {
          cacheStorage.set(where.key, create.embedding);
          return create;
        },
      },
      aiUsageLog: {
        create: async () => ({}),
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: dummyAiSettingMock,
    } as unknown as PrismaClient;

    const reliability = new Reliability();

    // Đợt 1: nạp 'text cũ 1' vào cache.
    await executeEmbedWithCache({
      provider: fakeProvider,
      reliability,
      store: mockStore,
      settings: dummySettings,
      texts: ['text cũ 1'],
      ctx: dummyContext,
    });
    const recorded1 = fakeProvider.recordedRequests[0];
    expect(recorded1).toBeDefined();
    if (!recorded1) throw new Error('Thiếu request đợt 1');
    expect(recorded1).toEqual(['text cũ 1']);

    fakeProvider.clearRecorded();

    // Đợt 2: mảng gồm ['text mới 1', 'text cũ 1', 'text mới 2'].
    const mixedResults = await executeEmbedWithCache({
      provider: fakeProvider,
      reliability,
      store: mockStore,
      settings: dummySettings,
      texts: ['text mới 1', 'text cũ 1', 'text mới 2'],
      ctx: dummyContext,
    });

    // Provider chỉ nhận đúng 2 chuỗi chưa từng có trong cache.
    expect(fakeProvider.recordedRequests).toHaveLength(1);
    const recorded2 = fakeProvider.recordedRequests[0];
    expect(recorded2).toBeDefined();
    if (!recorded2) throw new Error('Thiếu request đợt 2');
    expect(recorded2).toEqual(['text mới 1', 'text mới 2']);

    // Kết quả trả về phải đủ 3 phần tử theo đúng thứ tự mảng ban đầu.
    expect(mixedResults).toHaveLength(3);
  });
});
