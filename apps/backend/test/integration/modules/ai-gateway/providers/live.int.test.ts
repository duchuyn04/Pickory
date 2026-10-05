import { beforeAll, describe, expect, it } from 'vitest';
import { z } from 'zod';
import { createAiGateway } from '../../../../../src/modules/ai-gateway';
import {
  createDeepSeekProvider,
  createOpenAIEmbeddingProvider,
} from '../../../../../src/modules/ai-gateway/providers';
import type { ToolDefinition } from '../../../../../src/modules/ai-gateway/types';
import { loadConfig } from '../../../../../src/shared/config';

// Kiểm tra cờ biến môi trường để xác định có kích hoạt live test với API thật hay không.
const isLiveEnabled = process.env.AI_LIVE_TESTS === '1';

describe.skipIf(!isLiveEnabled)('AI Gateway Live Integration Tests (API thật)', () => {
  beforeAll(() => {
    // Khi đã chủ động bật AI_LIVE_TESTS=1, nếu thiếu khóa xác thực hoặc model thì phải báo lỗi ngay;
    // tuyệt đối không được tự động bỏ qua (skip) để tránh che giấu thiếu sót cấu hình môi trường.
    if (isLiveEnabled) {
      const config = loadConfig();
      const hasChatKey = Boolean(config.LLM_API_KEY.trim());
      const hasChatModel = Boolean(config.LLM_MODEL.trim());
      const hasEmbeddingKey = Boolean(config.EMBEDDING_API_KEY.trim());

      if (!hasChatKey || !hasChatModel || !hasEmbeddingKey) {
        throw new Error(
          'Điều kiện tiên quyết không thỏa mãn: thiếu LLM_API_KEY, LLM_MODEL hoặc EMBEDDING_API_KEY khi AI_LIVE_TESTS=1.',
        );
      }
    }
  });

  it('thực hiện chat sinh văn bản thực tế với DeepSeek và ghi nhận token usage', async () => {
    const config = loadConfig();
    const chatProvider = createDeepSeekProvider(config);
    const embeddingProvider = createOpenAIEmbeddingProvider(config);
    const gateway = createAiGateway({ chatProvider, embeddingProvider, config });

    // Gửi yêu cầu chat đơn giản tới model DeepSeek thật.
    const result = await gateway.chat(
      {
        messages: [{ role: 'user', content: 'Trả lời ngắn gọn: 1 + 1 bằng mấy?' }],
        temperature: 0.1,
      },
      {
        feature: 'shopping-assistant.chat',
        actorKey: 'user:live_test_user',
      },
    );

    // Kiểm tra phản hồi có văn bản thực tế và số lượng token tiêu thụ lớn hơn 0.
    expect(result.text.length).toBeGreaterThan(0);
    expect(result.usage.inputTokens).toBeGreaterThan(0);
    expect(result.usage.outputTokens).toBeGreaterThan(0);
  });

  it('thực hiện chatJson với DeepSeek và validate thành công schema Zod', async () => {
    const config = loadConfig();
    const chatProvider = createDeepSeekProvider(config);
    const embeddingProvider = createOpenAIEmbeddingProvider(config);
    const gateway = createAiGateway({ chatProvider, embeddingProvider, config });

    const answerSchema = z.object({
      result: z.number(),
      formula: z.string(),
    });

    // Yêu cầu DeepSeek sinh JSON khớp với schema Zod đã định nghĩa.
    const output = await gateway.chatJson(
      {
        messages: [
          {
            role: 'user',
            content: 'Tính 5 nhân 6 và trả về JSON có hai trường result (số) và formula (chuỗi).',
          },
        ],
      },
      answerSchema,
      {
        feature: 'personalized-content.description',
        actorKey: 'user:live_test_user',
      },
    );

    expect(output.result).toBe(30);
    expect(typeof output.formula).toBe('string');
  });

  it('thực hiện vòng lặp tool calling với DeepSeek thông qua ranh giới gateway', async () => {
    const config = loadConfig();
    const chatProvider = createDeepSeekProvider(config);
    const embeddingProvider = createOpenAIEmbeddingProvider(config);
    const gateway = createAiGateway({ chatProvider, embeddingProvider, config });

    let toolExecuted = false;
    const calculatorTool: ToolDefinition<{ x: number; y: number }> = {
      name: 'phepNhan',
      description: 'Hàm tính tích của hai số x và y',
      parameters: z.object({ x: z.number(), y: z.number() }),
      async execute({ x, y }) {
        toolExecuted = true;
        return { product: x * y };
      },
    };

    // Yêu cầu model gọi tool phepNhan để giải bài toán.
    const result = await gateway.chat(
      {
        messages: [{ role: 'user', content: 'Dùng công cụ phepNhan để tính 7 nhân 8' }],
        tools: [calculatorTool],
        maxSteps: 3,
      },
      {
        feature: 'shopping-assistant.chat',
        actorKey: 'user:live_test_user',
      },
    );

    // Gateway phải kích hoạt hàm execute của tool trong quá trình điều phối vòng lặp.
    expect(toolExecuted).toBe(true);
    expect(result.text.length).toBeGreaterThan(0);
  });

  it('sinh vector embedding thực tế từ OpenAI đạt chuẩn 1536 chiều hữu hạn', async () => {
    const config = loadConfig();
    const chatProvider = createDeepSeekProvider(config);
    const embeddingProvider = createOpenAIEmbeddingProvider(config);
    const gateway = createAiGateway({ chatProvider, embeddingProvider, config });

    // Gửi văn bản tiếng Việt để OpenAI sinh vector embedding.
    const vectors = await gateway.embed(['Tai nghe chống ồn Sony WH-1000XM5'], {
      feature: 'smart-search.index',
      actorKey: 'user:live_test_user',
    });

    // Xác nhận danh sách trả về đúng một vector.
    expect(vectors).toHaveLength(1);
    const firstVector = vectors[0];
    expect(firstVector).toBeDefined();
    // Bắt buộc vector phải đạt đúng 1536 chiều để phù hợp schema pgvector.
    expect(firstVector).toHaveLength(1536);

    // Kiểm tra từng tọa độ trong vector là số thực hữu hạn (không phải NaN hay Infinity).
    if (firstVector) {
      for (const coordinate of firstVector) {
        expect(typeof coordinate).toBe('number');
        expect(Number.isFinite(coordinate)).toBe(true);
      }
    }
  });
});
