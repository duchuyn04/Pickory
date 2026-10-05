import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import type { Config } from '../../../../../src/shared/config';
import { ProviderError } from '../../../../../src/modules/ai-gateway/errors';
import { createDeepSeekProvider } from '../../../../../src/modules/ai-gateway/providers/deepseek';
import type {
  ProviderChatRequest,
  ToolDefinition,
} from '../../../../../src/modules/ai-gateway/types';

describe('DeepSeek Chat Provider', () => {
  const dummyConfig: Config = {
    NODE_ENV: 'test',
    PORT: 3000,
    WEB_ORIGIN: 'http://localhost:5173',
    API_BASE_URL: 'http://localhost:3000',
    GOOGLE_CLIENT_ID: 'mock_id',
    GOOGLE_CLIENT_SECRET: 'mock_secret',
    TRUST_PROXY: false,
    DATABASE_URL: 'postgresql://user:pass@localhost:5432/db',
    LLM_MODEL: 'deepseek-flash',
    LLM_BASE_URL: 'https://mock.deepseek.com/v1',
    LLM_API_KEY: 'secret_deepseek_token',
    EMBEDDING_API_KEY: 'secret_openai_token',
    EMBEDDING_MODEL: 'text-embedding-3-small',
    AI_MONTHLY_BUDGET_USD: 30,
    AI_CHAT_DAILY_LIMIT_USER: 50,
    AI_CHAT_DAILY_LIMIT_ANON: 15,
    AI_LIVE_TESTS: false,
  };
  it('từ chối khởi tạo với lỗi mã 400 nếu LLM_MODEL bị để trống', () => {
    // Bắt buộc cấu hình model rõ ràng, không chấp nhận giá trị rỗng.
    expect(() => createDeepSeekProvider({ ...dummyConfig, LLM_MODEL: '' })).toThrow(ProviderError);
    try {
      createDeepSeekProvider({ ...dummyConfig, LLM_MODEL: '   ' });
    } catch (err) {
      expect(err).toBeInstanceOf(ProviderError);
      if (err instanceof ProviderError) {
        expect(err.status).toBe(400);
        expect(err.retryable).toBe(false);
      }
    }
  });

  it('phản ánh chính xác LLM_BASE_URL, LLM_MODEL và Authorization header trong HTTP request', async () => {
    let capturedUrl = '';
    let capturedAuth = '';
    let capturedBody: Record<string, unknown> = {};

    // Giả lập hàm fetch để xác thực ranh giới HTTP mà không phụ thuộc thư viện ngoài.
    const customFetch: typeof fetch = async (input, init) => {
      capturedUrl = String(input);
      const headers = new Headers(init?.headers);
      capturedAuth = headers.get('Authorization') ?? '';
      if (typeof init?.body === 'string') {
        capturedBody = JSON.parse(init.body) as Record<string, unknown>;
      }

      const mockResponse = {
        id: 'chatcmpl-test-1',
        object: 'chat.completion',
        created: 1234567890,
        model: 'deepseek-flash',
        choices: [
          {
            index: 0,
            message: { role: 'assistant', content: 'Phản hồi từ DeepSeek' },
            finish_reason: 'stop',
          },
        ],
        usage: {
          prompt_tokens: 18,
          completion_tokens: 9,
          total_tokens: 27,
          prompt_cache_hit_tokens: 6,
        },
      };

      return new Response(JSON.stringify(mockResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const provider = createDeepSeekProvider(dummyConfig, { fetch: customFetch });
    const request: ProviderChatRequest = {
      messages: [{ role: 'user', content: 'Xin chào Pickory' }],
    };

    const result = await provider.chat(request, new AbortController().signal);

    // Kiểm tra URL được gọi khớp với baseURL trong config.
    expect(capturedUrl).toBe('https://mock.deepseek.com/v1/chat/completions');
    // Khóa API được gửi dưới dạng Bearer token trong header Authorization.
    expect(capturedAuth).toBe('Bearer secret_deepseek_token');
    // Model được truyền trong body khớp với giá trị đã cấu hình.
    expect(capturedBody.model).toBe('deepseek-flash');
    // Nội dung văn bản và thống kê token được ánh xạ đầy đủ.
    expect(result.text).toBe('Phản hồi từ DeepSeek');
    expect(result.usage.inputTokens).toBe(18);
    expect(result.usage.outputTokens).toBe(9);
    expect(result.usage.cachedTokens).toBe(6);
  });

  it('tắt retry của SDK để chỉ gửi đúng một request khi gặp lỗi máy chủ 500 hoặc 429', async () => {
    let callCount = 0;
    const customFetch: typeof fetch = async () => {
      callCount += 1;
      return new Response(JSON.stringify({ error: { message: 'Overloaded' } }), {
        status: 500,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const provider = createDeepSeekProvider(dummyConfig, { fetch: customFetch });
    const request: ProviderChatRequest = {
      messages: [{ role: 'user', content: 'Kiểm tra retry' }],
    };

    // Khi gặp lỗi 500, adapter phải ném ProviderError ngay mà không tự retry thêm lần nào.
    await expect(provider.chat(request, new AbortController().signal)).rejects.toMatchObject({
      status: 500,
      retryable: true,
    });
    // Khẳng định số lượt gọi fetch đúng bằng một để chứng minh maxRetries bằng 0 đã hoạt động.
    expect(callCount).toBe(1);
  });

  it('làm sạch lỗi và không làm rò rỉ khóa xác thực hay thông điệp nhạy cảm', async () => {
    const customFetch: typeof fetch = async () => {
      return new Response(
        JSON.stringify({
          error: { message: 'Invalid API Key secret_deepseek_token provided for endpoint' },
        }),
        {
          status: 401,
          headers: { 'Content-Type': 'application/json' },
        },
      );
    };

    const provider = createDeepSeekProvider(dummyConfig, { fetch: customFetch });
    const request: ProviderChatRequest = {
      messages: [{ role: 'user', content: 'Thử lỗi 401' }],
    };

    let caughtError: unknown;
    try {
      await provider.chat(request, new AbortController().signal);
    } catch (err) {
      caughtError = err;
    }

    expect(caughtError).toBeInstanceOf(ProviderError);
    if (caughtError instanceof ProviderError) {
      expect(caughtError.status).toBe(401);
      expect(caughtError.retryable).toBe(false);
      // Thông điệp trả ra tuyệt đối không chứa khóa API để bảo vệ an ninh.
      expect(caughtError.message).not.toContain('secret_deepseek_token');
    }
  });

  it('kích hoạt JSON mode và trả về chuỗi JSON thô kể cả khi cú pháp chưa hoàn chỉnh', async () => {
    let capturedBody: Record<string, unknown> = {};
    const malformedJsonText = '{"title": "Áo sơ mi", "price": ';

    const customFetch: typeof fetch = async (_input, init) => {
      if (typeof init?.body === 'string') {
        capturedBody = JSON.parse(init.body) as Record<string, unknown>;
      }

      const mockResponse = {
        id: 'chatcmpl-json',
        object: 'chat.completion',
        created: 1234567890,
        model: 'deepseek-flash',
        choices: [
          {
            index: 0,
            message: { role: 'assistant', content: malformedJsonText },
            finish_reason: 'stop',
          },
        ],
        usage: { prompt_tokens: 20, completion_tokens: 15, total_tokens: 35 },
      };

      return new Response(JSON.stringify(mockResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const provider = createDeepSeekProvider(dummyConfig, { fetch: customFetch });
    const request: ProviderChatRequest = {
      messages: [{ role: 'user', content: 'Tạo sản phẩm JSON' }],
      json: true,
    };

    const result = await provider.chat(request, new AbortController().signal);

    // Xác nhận body request đã gửi cờ response_format json_object tới máy chủ.
    expect(capturedBody.response_format).toEqual({ type: 'json_object' });
    // Kết quả trả về chứa đúng văn bản JSON thô để gateway tự điều khiển lượt validate thứ hai.
    expect(result.text).toBe(malformedJsonText);
  });

  it('chuyển đổi và gửi danh sách tool kèm làm sạch PII trong mô tả tool', async () => {
    let capturedBody: Record<string, unknown> = {};

    const customFetch: typeof fetch = async (_input, init) => {
      if (typeof init?.body === 'string') {
        capturedBody = JSON.parse(init.body) as Record<string, unknown>;
      }

      const mockResponse = {
        id: 'chatcmpl-tool',
        object: 'chat.completion',
        created: 1234567890,
        model: 'deepseek-flash',
        choices: [
          {
            index: 0,
            message: {
              role: 'assistant',
              content: null,
              tool_calls: [
                {
                  id: 'call_search_123',
                  type: 'function',
                  function: {
                    name: 'timKiemSanPham',
                    arguments: '{"keyword":"laptop gaming"}',
                  },
                },
              ],
            },
            finish_reason: 'tool_calls',
          },
        ],
        usage: { prompt_tokens: 30, completion_tokens: 14, total_tokens: 44 },
      };

      return new Response(JSON.stringify(mockResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const testTool: ToolDefinition<{ keyword: string }> = {
      name: 'timKiemSanPham',
      description: 'Tìm kiếm sản phẩm cho khách hàng liên hệ qua 0912345678 hoặc dev@pickory.vn',
      parameters: z.object({
        keyword: z.string().describe('Từ khóa tìm kiếm hotline 0987654321'),
      }),
      async execute() {
        return [];
      },
    };

    const provider = createDeepSeekProvider(dummyConfig, { fetch: customFetch });
    const request: ProviderChatRequest = {
      messages: [{ role: 'user', content: 'Tìm laptop' }],
      tools: [testTool],
    };

    const result = await provider.chat(request, new AbortController().signal);

    // Kiểm tra danh sách tool được gửi đi trong request body.
    expect(Array.isArray(capturedBody.tools)).toBe(true);
    const sentTools = capturedBody.tools as Array<{
      function?: {
        description?: string;
        parameters?: {
          properties?: {
            keyword?: { description?: string };
          };
        };
      };
    }>;
    const sentDescription = sentTools[0]?.function?.description ?? '';
    // Số điện thoại và email trong mô tả tool đã được ẩn bằng [đã ẩn].
    expect(sentDescription).toContain('[đã ẩn]');
    expect(sentDescription).not.toContain('0912345678');
    expect(sentDescription).not.toContain('dev@pickory.vn');

    // PII bên trong mô tả của thuộc tính schema Zod cũng được ẩn sạch sẽ.
    const propDescription =
      sentTools[0]?.function?.parameters?.properties?.keyword?.description ?? '';
    expect(propDescription).toContain('[đã ẩn]');
    expect(propDescription).not.toContain('0987654321');

    // Kết quả trả về trích xuất chính xác thông tin tool call để gateway tự chạy bước tiếp theo.
    expect(result.toolCalls).toHaveLength(1);
    expect(result.toolCalls?.[0]).toEqual({
      id: 'call_search_123',
      name: 'timKiemSanPham',
      input: { keyword: 'laptop gaming' },
    });
  });

  it('không từ chối tool input sai schema ở mức provider để gateway nhận được đối số thô', async () => {
    const customFetch: typeof fetch = async () => {
      const mockResponse = {
        id: 'chatcmpl-invalid-args',
        object: 'chat.completion',
        created: 1234567890,
        model: 'deepseek-flash',
        choices: [
          {
            index: 0,
            message: {
              role: 'assistant',
              content: null,
              tool_calls: [
                {
                  id: 'call_invalid_1',
                  type: 'function',
                  function: {
                    name: 'taoDonHang',
                    // Số tiền là chuỗi thay vì số, sai định dạng mong đợi của schema
                    arguments: '{"amount":"khong_phai_so"}',
                  },
                },
              ],
            },
            finish_reason: 'tool_calls',
          },
        ],
        usage: { prompt_tokens: 15, completion_tokens: 8, total_tokens: 23 },
      };

      return new Response(JSON.stringify(mockResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const orderTool: ToolDefinition<{ amount: number }> = {
      name: 'taoDonHang',
      description: 'Tạo đơn hàng',
      parameters: z.object({ amount: z.number() }),
      async execute() {
        return {};
      },
    };

    const provider = createDeepSeekProvider(dummyConfig, { fetch: customFetch });
    const result = await provider.chat(
      {
        messages: [{ role: 'user', content: 'Tạo đơn hàng' }],
        tools: [orderTool],
      },
      new AbortController().signal,
    );

    // Provider trả về đối số thô mà không ném lỗi TypeValidationError để gateway chủ động xử lý
    expect(result.toolCalls).toHaveLength(1);
    expect(result.toolCalls?.[0]?.name).toBe('taoDonHang');
    expect(result.toolCalls?.[0]?.input).toEqual({ amount: 'khong_phai_so' });
  });

  it('phát luồng stream text thực tế và kết thúc bằng finish chunk chứa token usage', async () => {
    const sseLines = [
      'data: {"id":"chatcmpl-s","choices":[{"index":0,"delta":{"role":"assistant","content":"Xin "},"finish_reason":null}]}',
      'data: {"id":"chatcmpl-s","choices":[{"index":0,"delta":{"content":"chào "},"finish_reason":null}]}',
      'data: {"id":"chatcmpl-s","choices":[{"index":0,"delta":{"content":"bạn!"},"finish_reason":null}]}',
      'data: {"id":"chatcmpl-s","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":6,"total_tokens":18,"prompt_cache_hit_tokens":4}}',
      'data: [DONE]',
      '',
    ];

    const streamResponse = new ReadableStream({
      start(controller) {
        for (const line of sseLines) {
          controller.enqueue(new TextEncoder().encode(`${line}\n\n`));
        }
        controller.close();
      },
    });

    const customFetch: typeof fetch = async () => {
      return new Response(streamResponse, {
        status: 200,
        headers: { 'Content-Type': 'text/event-stream' },
      });
    };

    const provider = createDeepSeekProvider(dummyConfig, { fetch: customFetch });
    const chunks: Array<{ type: string; text?: string; usage?: unknown }> = [];

    for await (const chunk of provider.stream(
      { messages: [{ role: 'user', content: 'Chào' }] },
      new AbortController().signal,
    )) {
      chunks.push(chunk);
    }

    // Các text delta được phát ra theo đúng thứ tự luồng dữ liệu.
    expect(chunks[0]).toEqual({ type: 'text', text: 'Xin ' });
    expect(chunks[1]).toEqual({ type: 'text', text: 'chào ' });
    expect(chunks[2]).toEqual({ type: 'text', text: 'bạn!' });

    // Chunk cuối cùng là finish chunk mang thống kê token đã tiêu thụ.
    const finishChunk = chunks[3];
    expect(finishChunk?.type).toBe('finish');
    expect(finishChunk?.usage).toEqual({
      inputTokens: 12,
      outputTokens: 6,
      cachedTokens: 4,
    });
  });
  it('ném ProviderError nếu luồng stream hoàn tất nhưng hoàn toàn thiếu thông tin token usage', async () => {
    // SSE stream không gửi dòng usage ở chunk kết thúc
    const sseLines = [
      'data: {"id":"chatcmpl-no-usage","choices":[{"index":0,"delta":{"content":"Hi"},"finish_reason":null}]}',
      'data: {"id":"chatcmpl-no-usage","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}',
      'data: [DONE]',
      '',
    ];

    const streamResponse = new ReadableStream({
      start(controller) {
        for (const line of sseLines) {
          controller.enqueue(new TextEncoder().encode(`${line}\n\n`));
        }
        controller.close();
      },
    });

    const customFetch: typeof fetch = async () => {
      return new Response(streamResponse, {
        status: 200,
        headers: { 'Content-Type': 'text/event-stream' },
      });
    };

    const provider = createDeepSeekProvider(dummyConfig, { fetch: customFetch });
    const iterator = provider.stream(
      { messages: [{ role: 'user', content: 'Test thiếu usage' }] },
      new AbortController().signal,
    );

    await expect(async () => {
      // eslint-disable-next-line @typescript-eslint/no-unused-vars
      for await (const _ of iterator) {
        // Đọc hết stream
      }
    }).rejects.toMatchObject({
      status: undefined,
      retryable: false,
    });
  });

  it('lan truyền lỗi trong luồng stream dưới dạng ProviderError chuẩn hóa', async () => {
    const customFetch: typeof fetch = async () => {
      return new Response(JSON.stringify({ error: { message: 'Stream rejected' } }), {
        status: 429,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const provider = createDeepSeekProvider(dummyConfig, { fetch: customFetch });
    const iterator = provider.stream(
      { messages: [{ role: 'user', content: 'Thử lỗi stream' }] },
      new AbortController().signal,
    );

    await expect(async () => {
      // eslint-disable-next-line @typescript-eslint/no-unused-vars
      for await (const _ of iterator) {
        // Luồng stream gặp lỗi phải dừng ngay.
      }
    }).rejects.toMatchObject({
      status: 429,
      retryable: true,
    });
  });
});
