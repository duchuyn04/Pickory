import { createHash } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import type { PrismaClient } from '../../../../src/generated/prisma/client';
import {
  AiUnavailableError,
  createAiGateway,
  FakeChatProvider,
  FakeEmbeddingProvider,
} from '../../../../src/modules/ai-gateway';

describe('Lõi AI Gateway (AI-02 hợp đồng và độ tin cậy)', () => {
  it('ghi đúng một dòng log không chứa prompt và actorKeyHash là băm SHA-256', async () => {
    const fakeProvider = new FakeChatProvider();
    const recordedLogs: Record<string, unknown>[] = [];

    const mockStore = {
      aiUsageLog: {
        create: async ({ data }: { data: Record<string, unknown> }) => {
          recordedLogs.push(data);
          return data;
        },
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: {
        findUnique: async () => ({
          id: 'singleton',
          monthlyBudgetUsd: 50,
          chatDailyLimitUser: 50,
          chatDailyLimitAnon: 15,
        }),
      },
    } as unknown as PrismaClient;

    const gateway = createAiGateway({
      chatProvider: fakeProvider,
      embeddingProvider: new FakeEmbeddingProvider(),
      store: mockStore,
    });

    const actorId = 'user:customer_vip_777';
    const result = await gateway.chat(
      { messages: [{ role: 'user', content: 'Xin chào bot bí mật' }] },
      { feature: 'shopping-assistant.chat', actorKey: actorId },
    );

    // Kiểm tra chính xác nội dung trả về từ fake provider.
    expect(result.text).toBe('Xin chào từ fake AI');

    expect(recordedLogs).toHaveLength(1);
    const log = recordedLogs[0];
    expect(log).toBeDefined();
    if (!log) throw new Error('Thiếu dòng nhật ký sử dụng');

    expect(log.status).toBe('ok');
    // Tuyệt đối không lưu prompt hoặc câu trả lời vào nhật ký sử dụng.
    expect(log.prompt).toBeUndefined();
    expect(log.content).toBeUndefined();

    // actorKeyHash phải khớp với hash SHA-256 tự tính toán bằng crypto độc lập.
    const expectedDirectHash = createHash('sha256').update(actorId).digest('hex');
    expect(log.actorKeyHash).toBe(expectedDirectHash);
  });

  it('làm sạch PII trước khi gửi tới provider', async () => {
    const fakeProvider = new FakeChatProvider();

    const mockStore = {
      aiUsageLog: {
        create: async () => ({}),
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: {
        findUnique: async () => ({
          id: 'singleton',
          monthlyBudgetUsd: 50,
          chatDailyLimitUser: 50,
          chatDailyLimitAnon: 15,
        }),
      },
    } as unknown as PrismaClient;

    const gateway = createAiGateway({
      chatProvider: fakeProvider,
      store: mockStore,
    });

    await gateway.chat(
      {
        messages: [
          {
            role: 'user',
            content: 'Số điện thoại của tôi là 0912 345 678 và email a@b.vn cần tư vấn.',
          },
        ],
      },
      { feature: 'shopping-assistant.chat', actorKey: 'user:client_001' },
    );

    // Provider nhận văn bản đã được che giấu qua redactPII.
    expect(fakeProvider.recordedRequests).toHaveLength(1);
    const recordedRequest = fakeProvider.recordedRequests[0];
    expect(recordedRequest).toBeDefined();
    if (!recordedRequest) throw new Error('Thiếu request đã ghi lại');

    const firstMessage = recordedRequest.messages[0];
    expect(firstMessage).toBeDefined();
    if (!firstMessage) throw new Error('Thiếu tin nhắn đầu tiên');

    const sentMessage = firstMessage.content;
    expect(sentMessage).not.toContain('0912 345 678');
    expect(sentMessage).not.toContain('a@b.vn');
    expect(sentMessage).toContain('[đã ẩn]');
  });

  it('từ chối trước khi gọi provider nếu model chưa có bảng giá và ghi log error với chi phí 0 USD', async () => {
    const unpricedProvider = new FakeChatProvider({ model: 'unlisted-expensive-model' });
    const recordedLogs: Record<string, unknown>[] = [];

    const mockStore = {
      aiUsageLog: {
        create: async ({ data }: { data: Record<string, unknown> }) => {
          recordedLogs.push(data);
          return data;
        },
      },
      aiSetting: {
        findUnique: async () => ({
          id: 'singleton',
          monthlyBudgetUsd: 50,
          chatDailyLimitUser: 50,
          chatDailyLimitAnon: 15,
        }),
      },
    } as unknown as PrismaClient;

    const gateway = createAiGateway({
      chatProvider: unpricedProvider,
      store: mockStore,
    });

    // Bị chặn trước khi gọi provider để đảm bảo an toàn chi phí.
    await expect(
      gateway.chat(
        { messages: [{ role: 'user', content: 'Thử model không có giá' }] },
        { feature: 'shopping-assistant.chat', actorKey: 'user:client_test' },
      ),
    ).rejects.toBeInstanceOf(Error);

    // Provider không bị gọi.
    expect(unpricedProvider.recordedRequests).toHaveLength(0);

    // Ghi nhận 1 dòng log trạng thái error với chi phí 0 USD.
    expect(recordedLogs).toHaveLength(1);
    const errorLog = recordedLogs[0];
    expect(errorLog).toBeDefined();
    if (!errorLog) throw new Error('Thiếu log báo lỗi');
    expect(errorLog.status).toBe('error');
    expect(errorLog.costUsd).toBe(0);
    expect(errorLog.errorCode).toBe('UNKNOWN_MODEL_PRICING');
  });

  it('kiểm tra tính hợp lệ của tham số maxSteps và actorKey', async () => {
    const fakeProvider = new FakeChatProvider();
    const mockStore = {
      aiUsageLog: { create: async () => ({}) },
      aiSetting: {
        findUnique: async () => ({
          id: 'singleton',
          monthlyBudgetUsd: 50,
          chatDailyLimitUser: 50,
          chatDailyLimitAnon: 15,
        }),
      },
    } as unknown as PrismaClient;

    const gateway = createAiGateway({
      chatProvider: fakeProvider,
      store: mockStore,
    });

    // maxSteps không phải số nguyên dương phải báo lỗi 400.
    await expect(
      gateway.chat(
        { messages: [{ role: 'user', content: 'Chào' }], maxSteps: -1 },
        { feature: 'shopping-assistant.chat', actorKey: 'user:ok_user' },
      ),
    ).rejects.toMatchObject({ code: 'INVALID_MAX_STEPS' });

    // shopping-assistant.chat thiếu actorKey phải báo lỗi 400.
    await expect(
      gateway.chat(
        { messages: [{ role: 'user', content: 'Chào' }] },
        { feature: 'shopping-assistant.chat' },
      ),
    ).rejects.toMatchObject({ code: 'INVALID_ACTOR_KEY' });

    // actorKey sai định dạng tiền tố ở feature khác cũng bị từ chối sớm.
    await expect(
      gateway.chat(
        { messages: [{ role: 'user', content: 'Chào' }] },
        { feature: 'smart-search.parse-query', actorKey: 'invalid_prefix_key' },
      ),
    ).rejects.toMatchObject({ code: 'INVALID_ACTOR_KEY' });
  });

  it('lời gọi lỗi hoặc bị chặn mạch vẫn ghi một dòng log', async () => {
    const fakeProvider = new FakeChatProvider();
    fakeProvider.enqueueError(new Error('Lỗi mạng'));

    const recordedLogs: Record<string, unknown>[] = [];
    const mockStore = {
      aiUsageLog: {
        create: async ({ data }: { data: Record<string, unknown> }) => {
          recordedLogs.push(data);
          return data;
        },
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: {
        findUnique: async () => ({
          id: 'singleton',
          monthlyBudgetUsd: 50,
          chatDailyLimitUser: 50,
          chatDailyLimitAnon: 15,
        }),
      },
    } as unknown as PrismaClient;

    const gateway = createAiGateway({
      chatProvider: fakeProvider,
      store: mockStore,
    });

    await expect(
      gateway.chat(
        { messages: [{ role: 'user', content: 'Gây lỗi' }] },
        { feature: 'shopping-assistant.chat', actorKey: 'user:client_002' },
      ),
    ).rejects.toThrow();

    // Vẫn ghi nhận nhật ký lỗi trạng thái error.
    expect(recordedLogs).toHaveLength(1);
    const errorLog = recordedLogs[0];
    expect(errorLog).toBeDefined();
    if (!errorLog) throw new Error('Thiếu dòng nhật ký lỗi');
    expect(errorLog.status).toBe('error');
  });

  it('môi trường production thiếu API key từ chối phục vụ, không giả lập thành công', async () => {
    const mockStore = {
      aiSetting: {
        findUnique: async () => ({
          id: 'singleton',
          monthlyBudgetUsd: 50,
          chatDailyLimitUser: 50,
          chatDailyLimitAnon: 15,
        }),
      },
      aiUsageLog: {
        create: async () => ({}),
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
    } as unknown as PrismaClient;

    const prodGateway = createAiGateway({
      store: mockStore,
      config: {
        NODE_ENV: 'production',
        PORT: 3000,
        WEB_ORIGIN: 'http://localhost:5173',
        API_BASE_URL: 'http://localhost:3000',
        GOOGLE_CLIENT_ID: 'id',
        GOOGLE_CLIENT_SECRET: 'secret',
        TRUST_PROXY: false,
        DATABASE_URL: 'postgresql://test',
        LLM_MODEL: '',
        LLM_BASE_URL: 'https://api.deepseek.com',
        LLM_API_KEY: '',
        EMBEDDING_API_KEY: '',
        EMBEDDING_MODEL: 'text-embedding-3-small',
        AI_MONTHLY_BUDGET_USD: 30,
        AI_CHAT_DAILY_LIMIT_USER: 50,
        AI_CHAT_DAILY_LIMIT_ANON: 15,
        AI_LIVE_TESTS: false,
      },
    });

    // Production thiếu key phải ném AiUnavailableError ngay lập tức.
    await expect(
      prodGateway.chat(
        { messages: [{ role: 'user', content: 'Tin nhắn' }] },
        { feature: 'shopping-assistant.chat', actorKey: 'user:member_prod' },
      ),
    ).rejects.toBeInstanceOf(AiUnavailableError);
  });
});
