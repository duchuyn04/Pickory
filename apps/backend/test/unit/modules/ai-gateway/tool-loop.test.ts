import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import type { PrismaClient } from '../../../../src/generated/prisma/client';
import { AiBudgetExceededError } from '../../../../src/modules/ai-gateway/errors';
import { FakeChatProvider } from '../../../../src/modules/ai-gateway/fake-provider';
import { Reliability } from '../../../../src/modules/ai-gateway/reliability';
import {
  executeChatStreamWithToolLoop,
  executeChatWithToolLoop,
} from '../../../../src/modules/ai-gateway/tool-loop';
import type {
  AiSettings,
  CallContext,
  ToolDefinition,
} from '../../../../src/modules/ai-gateway/types';

describe('Vòng lặp công cụ đa bước (tool loop)', () => {
  const dummyContext: CallContext = {
    feature: 'shopping-assistant.chat',
    actorKey: 'user:customer_vip_001',
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

  it('chạy 3 bước ghi đúng 3 dòng log và dừng tại maxSteps dù model còn muốn gọi công cụ', async () => {
    const fakeProvider = new FakeChatProvider();

    // Bước 1: gọi công cụ tra cứu.
    fakeProvider.enqueueResponse({
      text: 'Đang tra cứu giá...',
      usage: { inputTokens: 20, outputTokens: 10, cachedTokens: 0 },
      toolCalls: [{ id: 'tc1', name: 'lookup_price', input: { item: 'iPhone 15' } }],
    });
    // Bước 2: gọi công cụ tồn kho.
    fakeProvider.enqueueResponse({
      text: 'Đang kiểm tra kho...',
      usage: { inputTokens: 40, outputTokens: 15, cachedTokens: 0 },
      toolCalls: [{ id: 'tc2', name: 'check_stock', input: { item: 'iPhone 15' } }],
    });
    // Bước 3: model vẫn muốn gọi thêm công cụ nữa nhưng maxSteps = 3 nên phải dừng.
    fakeProvider.enqueueResponse({
      text: 'Giá là 20 triệu và còn hàng.',
      usage: { inputTokens: 60, outputTokens: 20, cachedTokens: 0 },
      toolCalls: [{ id: 'tc3', name: 'lookup_discount', input: { item: 'iPhone 15' } }],
    });

    const sampleTools: ToolDefinition[] = [
      {
        name: 'lookup_price',
        description: 'Tra cứu giá sản phẩm',
        parameters: z.object({ item: z.string() }),
        async execute({ item }) {
          return `${item}: 20.000.000 VND`;
        },
      },
      {
        name: 'check_stock',
        description: 'Kiểm tra tồn kho',
        parameters: z.object({ item: z.string() }),
        async execute({ item }) {
          return `${item}: Còn 5 máy`;
        },
      },
    ];

    const loggedEntries: unknown[] = [];
    const mockStore = {
      aiUsageLog: {
        create: async (args: unknown) => {
          loggedEntries.push(args);
          return {};
        },
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: dummyAiSettingMock,
    } as unknown as PrismaClient;

    const result = await executeChatWithToolLoop({
      provider: fakeProvider,
      reliability: new Reliability(),
      store: mockStore,
      settings: dummySettings,
      req: {
        messages: [{ role: 'user', content: 'Tìm iPhone 15' }],
        tools: sampleTools,
        maxSteps: 3,
      },
      ctx: dummyContext,
    });

    expect(result.text).toBe('Giá là 20 triệu và còn hàng.');
    // Đúng 3 bước gọi provider và đúng 3 dòng nhật ký sử dụng.
    expect(fakeProvider.recordedRequests).toHaveLength(3);
    expect(loggedEntries).toHaveLength(3);
  });

  it('ẩn SĐT hoặc email trong kết quả công cụ trước khi gửi lại cho LLM', async () => {
    const fakeProvider = new FakeChatProvider();

    // Bước 1: gọi công cụ trả về thông tin chứa số điện thoại và email cá nhân.
    fakeProvider.enqueueResponse({
      text: 'Đang gọi công cụ...',
      usage: { inputTokens: 15, outputTokens: 5, cachedTokens: 0 },
      toolCalls: [{ id: 'tc_info', name: 'get_contact', input: {} }],
    });
    // Bước 2: phản hồi cuối cùng.
    fakeProvider.enqueueResponse({
      text: 'Đã hoàn tất tra cứu.',
      usage: { inputTokens: 30, outputTokens: 10, cachedTokens: 0 },
    });

    const contactTool: ToolDefinition = {
      name: 'get_contact',
      description: 'Lấy thông tin liên hệ hỗ trợ',
      parameters: z.object({}),
      async execute() {
        return 'Liên hệ hotline 0912 345 678 hoặc email support@pickory.vn để gặp nhân viên.';
      },
    };

    const mockStore = {
      aiUsageLog: {
        create: async () => ({}),
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: dummyAiSettingMock,
    } as unknown as PrismaClient;

    await executeChatWithToolLoop({
      provider: fakeProvider,
      reliability: new Reliability(),
      store: mockStore,
      settings: dummySettings,
      req: {
        messages: [{ role: 'user', content: 'Cần số điện thoại hỗ trợ' }],
        tools: [contactTool],
        maxSteps: 2,
      },
      ctx: dummyContext,
    });

    // Kiểm tra request bước 2 gửi cho provider: kết quả của tool đã bị ẩn PII.
    const step2Request = fakeProvider.recordedRequests[1];
    expect(step2Request).toBeDefined();
    if (!step2Request) throw new Error('Thiếu request bước 2');

    const toolResultMessage = step2Request.messages.find((m) => m.role === 'tool');
    expect(toolResultMessage).toBeDefined();
    expect(toolResultMessage?.content).not.toContain('0912 345 678');
    expect(toolResultMessage?.content).not.toContain('support@pickory.vn');
    expect(toolResultMessage?.content).toContain('[đã ẩn]');
  });

  it('xử lý công cụ ném lỗi thành phản hồi thông báo an toàn, không làm sập vòng lặp', async () => {
    const fakeProvider = new FakeChatProvider();

    // Bước 1: gọi công cụ bị lỗi kết nối cơ sở dữ liệu.
    fakeProvider.enqueueResponse({
      text: 'Đang truy vấn kho...',
      usage: { inputTokens: 10, outputTokens: 5, cachedTokens: 0 },
      toolCalls: [{ id: 'tc_err', name: 'failing_tool', input: {} }],
    });
    // Bước 2: model nhận thông báo lỗi từ tool và thông báo lại cho người dùng.
    fakeProvider.enqueueResponse({
      text: 'Hệ thống kho đang bận, xin quý khách thử lại sau.',
      usage: { inputTokens: 25, outputTokens: 12, cachedTokens: 0 },
    });

    const errorTool: ToolDefinition = {
      name: 'failing_tool',
      description: 'Công cụ gặp sự cố',
      parameters: z.object({}),
      async execute() {
        throw new Error('Mất kết nối tới máy chủ kho');
      },
    };

    const mockStore = {
      aiUsageLog: {
        create: async () => ({}),
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: dummyAiSettingMock,
    } as unknown as PrismaClient;

    const result = await executeChatWithToolLoop({
      provider: fakeProvider,
      reliability: new Reliability(),
      store: mockStore,
      settings: dummySettings,
      req: {
        messages: [{ role: 'user', content: 'Xem hàng' }],
        tools: [errorTool],
        maxSteps: 2,
      },
      ctx: dummyContext,
    });

    expect(result.text).toBe('Hệ thống kho đang bận, xin quý khách thử lại sau.');
    const step2Request = fakeProvider.recordedRequests[1];
    expect(step2Request).toBeDefined();
    if (!step2Request) throw new Error('Thiếu request bước 2');

    const toolMessage = step2Request.messages.find((m) => m.role === 'tool');
    expect(toolMessage?.content).toContain(
      'Thao tác thực thi công cụ "failing_tool" không thành công',
    );
  });

  it('dừng vòng lặp và ném AiBudgetExceededError khi ngân sách chạm ngưỡng giữa chừng', async () => {
    const fakeProvider = new FakeChatProvider();

    // Bước 1: chạy thành công.
    fakeProvider.enqueueResponse({
      text: 'Bước 1 hoàn tất',
      usage: { inputTokens: 10, outputTokens: 5, cachedTokens: 0 },
      toolCalls: [{ id: 'tc1', name: 'dummy_tool', input: {} }],
    });

    let currentCost = 50; // Dưới 120%
    const mockStore = {
      aiUsageLog: {
        create: async () => ({}),
        aggregate: async () => {
          // Khi bước 2 kiểm tra, chi phí đã vọt lên 130 USD (vượt 120% của 100 USD).
          return { _sum: { costUsd: currentCost } };
        },
      },
      aiSetting: dummyAiSettingMock,
    } as unknown as PrismaClient;

    const dummyTool: ToolDefinition = {
      name: 'dummy_tool',
      description: 'Công cụ giả lập',
      parameters: z.object({}),
      async execute() {
        // Tăng chi phí mô phỏng việc ngân sách chạm ngưỡng giữa hai bước.
        currentCost = 130;
        return 'kết quả bước 1';
      },
    };

    await expect(
      executeChatWithToolLoop({
        provider: fakeProvider,
        reliability: new Reliability(),
        store: mockStore,
        settings: dummySettings,
        req: {
          messages: [{ role: 'user', content: 'Bắt đầu' }],
          tools: [dummyTool],
          maxSteps: 3,
        },
        ctx: dummyContext,
      }),
    ).rejects.toBeInstanceOf(AiBudgetExceededError);

    // Chỉ thực hiện đúng 1 bước trước khi bị chặn ở bước 2.
    expect(fakeProvider.recordedRequests).toHaveLength(1);
  });

  it('phát chunk văn bản theo thời gian thực (real streaming)', async () => {
    const fakeProvider = new FakeChatProvider();
    fakeProvider.enqueueStreamChunks([
      { type: 'text', text: 'Xin chào ' },
      { type: 'text', text: 'bạn, ' },
      { type: 'text', text: 'tôi là trợ lý.' },
      {
        type: 'finish',
        usage: { inputTokens: 10, outputTokens: 8, cachedTokens: 0 },
      },
    ]);

    const mockStore = {
      aiUsageLog: {
        create: async () => ({}),
        aggregate: async () => ({ _sum: { costUsd: 0 } }),
      },
      aiSetting: dummyAiSettingMock,
    } as unknown as PrismaClient;

    const stream = executeChatStreamWithToolLoop({
      provider: fakeProvider,
      reliability: new Reliability(),
      store: mockStore,
      settings: dummySettings,
      req: { messages: [{ role: 'user', content: 'Chào bot' }] },
      ctx: dummyContext,
    });

    const receivedTexts: string[] = [];
    for await (const chunk of stream) {
      if (chunk.type === 'text') {
        receivedTexts.push(chunk.text);
      }
    }

    expect(receivedTexts).toEqual(['Xin chào ', 'bạn, ', 'tôi là trợ lý.']);
  });
});
