import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import type { PrismaClient } from '../../../../src/generated/prisma/client';
import { executeChatJson } from '../../../../src/modules/ai-gateway/chat-json';
import { AiInvalidOutputError } from '../../../../src/modules/ai-gateway/errors';
import { FakeChatProvider } from '../../../../src/modules/ai-gateway/fake-provider';
import { Reliability } from '../../../../src/modules/ai-gateway/reliability';
import type { AiSettings, CallContext } from '../../../../src/modules/ai-gateway/types';

describe('Thực thi Chat JSON có cấu trúc (chatJson)', () => {
  const dummyContext: CallContext = {
    feature: 'personalized-content.description',
    actorKey: 'user:staff_marketing_1',
  };

  const dummySettings: AiSettings = {
    monthlyBudgetUsd: 100,
    chatDailyLimitUser: 50,
    chatDailyLimitAnon: 15,
  };

  const itemSchema = z.object({
    title: z.string(),
    price: z.number(),
  });

  it('trả về đối tượng đúng schema ngay lần gọi đầu tiên và ghi đúng một dòng log', async () => {
    const fakeProvider = new FakeChatProvider();
    fakeProvider.enqueueResponse({
      text: JSON.stringify({ title: 'Áo thun cotton', price: 150000 }),
      usage: { inputTokens: 50, outputTokens: 20, cachedTokens: 0 },
    });

    const loggedEntries: unknown[] = [];
    const mockStore = {
      aiUsageLog: {
        create: async (args: unknown) => {
          loggedEntries.push(args);
          return {};
        },
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: {
        findUnique: async () => ({
          id: 'singleton',
          monthlyBudgetUsd: dummySettings.monthlyBudgetUsd,
          chatDailyLimitUser: dummySettings.chatDailyLimitUser,
          chatDailyLimitAnon: dummySettings.chatDailyLimitAnon,
        }),
      },
    } as unknown as PrismaClient;

    const result = await executeChatJson({
      provider: fakeProvider,
      reliability: new Reliability(),
      store: mockStore,
      settings: dummySettings,
      req: { messages: [{ role: 'user', content: 'Tạo sản phẩm mẫu' }] },
      schema: itemSchema,
      ctx: dummyContext,
    });

    expect(result).toEqual({ title: 'Áo thun cotton', price: 150000 });
    // Đúng một lần gọi tới provider và đúng một dòng log được ghi.
    expect(fakeProvider.recordedRequests).toHaveLength(1);
    expect(loggedEntries).toHaveLength(1);
  });

  it('thử lại đúng một lần khi sai schema lần đầu và thành công ở lần hai', async () => {
    const fakeProvider = new FakeChatProvider();
    // Lần 1: trả về thiếu trường price (sai schema).
    fakeProvider.enqueueResponse({
      text: JSON.stringify({ title: 'Áo thun lỗi' }),
      usage: { inputTokens: 40, outputTokens: 15, cachedTokens: 0 },
    });
    // Lần 2: sửa lại đúng schema có cả title và price.
    fakeProvider.enqueueResponse({
      text: JSON.stringify({ title: 'Áo thun chuẩn', price: 180000 }),
      usage: { inputTokens: 60, outputTokens: 25, cachedTokens: 0 },
    });

    const loggedEntries: unknown[] = [];
    const mockStore = {
      aiUsageLog: {
        create: async (args: unknown) => {
          loggedEntries.push(args);
          return {};
        },
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: {
        findUnique: async () => ({
          id: 'singleton',
          monthlyBudgetUsd: dummySettings.monthlyBudgetUsd,
          chatDailyLimitUser: dummySettings.chatDailyLimitUser,
          chatDailyLimitAnon: dummySettings.chatDailyLimitAnon,
        }),
      },
    } as unknown as PrismaClient;

    const result = await executeChatJson({
      provider: fakeProvider,
      reliability: new Reliability(),
      store: mockStore,
      settings: dummySettings,
      req: { messages: [{ role: 'user', content: 'Tạo sản phẩm' }] },
      schema: itemSchema,
      ctx: dummyContext,
    });

    expect(result).toEqual({ title: 'Áo thun chuẩn', price: 180000 });
    // Đã gọi 2 lần và ghi đúng 2 dòng log.
    expect(fakeProvider.recordedRequests).toHaveLength(2);
    expect(loggedEntries).toHaveLength(2);
  });

  it('ném lỗi AiInvalidOutputError sau đúng 2 lần thử thất bại liên tiếp', async () => {
    const fakeProvider = new FakeChatProvider();
    // Cả 2 lần đều trả về chuỗi JSON sai kiểu dữ liệu price.
    fakeProvider.enqueueResponse({
      text: JSON.stringify({ title: 'Áo', price: 'không phải số' }),
      usage: { inputTokens: 30, outputTokens: 10, cachedTokens: 0 },
    });
    fakeProvider.enqueueResponse({
      text: JSON.stringify({ title: 'Vẫn sai', price: 'vẫn không phải số' }),
      usage: { inputTokens: 45, outputTokens: 12, cachedTokens: 0 },
    });

    const loggedEntries: unknown[] = [];
    const mockStore = {
      aiUsageLog: {
        create: async (args: unknown) => {
          loggedEntries.push(args);
          return {};
        },
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: {
        findUnique: async () => ({
          id: 'singleton',
          monthlyBudgetUsd: dummySettings.monthlyBudgetUsd,
          chatDailyLimitUser: dummySettings.chatDailyLimitUser,
          chatDailyLimitAnon: dummySettings.chatDailyLimitAnon,
        }),
      },
    } as unknown as PrismaClient;

    await expect(
      executeChatJson({
        provider: fakeProvider,
        reliability: new Reliability(),
        store: mockStore,
        settings: dummySettings,
        req: { messages: [{ role: 'user', content: 'Tạo sản phẩm' }] },
        schema: itemSchema,
        ctx: dummyContext,
      }),
    ).rejects.toBeInstanceOf(AiInvalidOutputError);

    // Xác nhận dừng đúng sau 2 lần thử và có đủ 2 dòng nhật ký.
    expect(fakeProvider.recordedRequests).toHaveLength(2);
    expect(loggedEntries).toHaveLength(2);
    expect(loggedEntries).toMatchObject([
      { data: { status: 'error', errorCode: 'AI_INVALID_OUTPUT' } },
      { data: { status: 'error', errorCode: 'AI_INVALID_OUTPUT' } },
    ]);
  });
});
