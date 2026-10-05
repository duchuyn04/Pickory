import { describe, expect, it } from 'vitest';
import type { Config } from '../../../../../src/shared/config';
import { ProviderError } from '../../../../../src/modules/ai-gateway/errors';
import { createOpenAIEmbeddingProvider } from '../../../../../src/modules/ai-gateway/providers/openai-embedding';

describe('OpenAI Embedding Provider', () => {
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
    LLM_BASE_URL: 'https://api.deepseek.com',
    LLM_API_KEY: 'secret_deepseek_token',
    EMBEDDING_API_KEY: 'secret_openai_token',
    EMBEDDING_MODEL: 'text-embedding-3-small',
    AI_MONTHLY_BUDGET_USD: 30,
    AI_CHAT_DAILY_LIMIT_USER: 50,
    AI_CHAT_DAILY_LIMIT_ANON: 15,
    AI_LIVE_TESTS: false,
  };
  it('từ chối khởi tạo với lỗi mã 400 nếu EMBEDDING_MODEL bị để trống', () => {
    // Bắt buộc cấu hình tên model embedding rõ ràng, không dùng giá trị rỗng.
    expect(() => createOpenAIEmbeddingProvider({ ...dummyConfig, EMBEDDING_MODEL: '' })).toThrow(
      ProviderError,
    );
    try {
      createOpenAIEmbeddingProvider({ ...dummyConfig, EMBEDDING_MODEL: '   ' });
    } catch (err) {
      expect(err).toBeInstanceOf(ProviderError);
      if (err instanceof ProviderError) {
        expect(err.status).toBe(400);
        expect(err.retryable).toBe(false);
      }
    }
  });

  it('phản ánh chính xác EMBEDDING_MODEL và Authorization header trong HTTP request', async () => {
    let capturedUrl = '';
    let capturedAuth = '';
    let capturedBody: Record<string, unknown> = {};

    const customFetch: typeof fetch = async (input, init) => {
      capturedUrl = String(input);
      const headers = new Headers(init?.headers);
      capturedAuth = headers.get('Authorization') ?? '';
      if (typeof init?.body === 'string') {
        capturedBody = JSON.parse(init.body) as Record<string, unknown>;
      }

      // Tạo một vector hợp lệ có đúng 1536 chiều với các số thực hữu hạn.
      const sampleVector = Array.from({ length: 1536 }, () => 0.0123);
      const mockResponse = {
        object: 'list',
        data: [{ object: 'embedding', index: 0, embedding: sampleVector }],
        model: 'text-embedding-3-small',
        usage: { prompt_tokens: 15, total_tokens: 15 },
      };

      return new Response(JSON.stringify(mockResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const provider = createOpenAIEmbeddingProvider(dummyConfig, { fetch: customFetch });
    const result = await provider.embed(['Văn bản cần sinh vector'], new AbortController().signal);

    // Kiểm tra endpoint chuẩn của OpenAI embedding.
    expect(capturedUrl).toBe('https://api.openai.com/v1/embeddings');
    // Header Authorization mang token xác thực.
    expect(capturedAuth).toBe('Bearer secret_openai_token');
    // Model và số chiều vector 1536 được truyền trong body đúng cấu hình.
    expect(capturedBody.model).toBe('text-embedding-3-small');
    expect(capturedBody.dimensions).toBe(1536);
    expect(result.vectors).toHaveLength(1);
    expect(result.vectors[0]).toHaveLength(1536);
    expect(result.usage.inputTokens).toBe(15);
  });

  it('trả về mảng rỗng ngay lập tức mà không gọi HTTP khi danh sách đầu vào rỗng', async () => {
    let fetchCalled = false;
    const customFetch: typeof fetch = async () => {
      fetchCalled = true;
      return new Response('{}', { status: 200 });
    };

    const provider = createOpenAIEmbeddingProvider(dummyConfig, { fetch: customFetch });
    const result = await provider.embed([], new AbortController().signal);

    expect(result.vectors).toEqual([]);
    expect(result.usage).toEqual({ inputTokens: 0, outputTokens: 0, cachedTokens: 0 });
    // Không được phát sinh bất kỳ lượt gọi HTTP nào.
    expect(fetchCalled).toBe(false);
  });

  it('từ chối vector không đủ 1536 chiều bằng ProviderError không retry', async () => {
    const customFetch: typeof fetch = async () => {
      // Giả lập model trả về vector chỉ có 512 chiều (sai cấu trúc so với cột DB 1536).
      const invalidVector = Array.from({ length: 512 }, () => 0.5);
      const mockResponse = {
        object: 'list',
        data: [{ object: 'embedding', index: 0, embedding: invalidVector }],
        model: 'text-embedding-3-small',
        usage: { prompt_tokens: 10, total_tokens: 10 },
      };
      return new Response(JSON.stringify(mockResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const provider = createOpenAIEmbeddingProvider(dummyConfig, { fetch: customFetch });

    await expect(
      provider.embed(['Đoạn text kiểm tra kích thước'], new AbortController().signal),
    ).rejects.toMatchObject({
      retryable: false,
    });
  });

  it('từ chối vector chứa tọa độ không phải số hữu hạn (NaN hoặc Infinity)', async () => {
    const customFetch: typeof fetch = async () => {
      const invalidVector = Array.from({ length: 1536 }, () => 0.1);
      // Gán một tọa độ là null hoặc NaN khi decode.
      (invalidVector as unknown[])[10] = 'not_a_number';

      const mockResponse = {
        object: 'list',
        data: [{ object: 'embedding', index: 0, embedding: invalidVector }],
        model: 'text-embedding-3-small',
        usage: { prompt_tokens: 10, total_tokens: 10 },
      };
      return new Response(JSON.stringify(mockResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const provider = createOpenAIEmbeddingProvider(dummyConfig, { fetch: customFetch });

    await expect(
      provider.embed(['Đoạn text kiểm tra tọa độ'], new AbortController().signal),
    ).rejects.toMatchObject({
      retryable: false,
    });
  });

  it('từ chối phản hồi có số lượng vector không khớp với số lượng văn bản đầu vào', async () => {
    const customFetch: typeof fetch = async () => {
      // Gửi 2 văn bản nhưng máy chủ chỉ trả về 1 vector.
      const sampleVector = Array.from({ length: 1536 }, () => 0.05);
      const mockResponse = {
        object: 'list',
        data: [{ object: 'embedding', index: 0, embedding: sampleVector }],
        model: 'text-embedding-3-small',
        usage: { prompt_tokens: 10, total_tokens: 10 },
      };
      return new Response(JSON.stringify(mockResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const provider = createOpenAIEmbeddingProvider(dummyConfig, { fetch: customFetch });

    await expect(
      provider.embed(['Văn bản thứ nhất', 'Văn bản thứ hai'], new AbortController().signal),
    ).rejects.toMatchObject({
      retryable: false,
    });
  });

  it('tắt retry của SDK để chỉ gửi đúng một request khi gặp mã lỗi 500', async () => {
    let callCount = 0;
    const customFetch: typeof fetch = async () => {
      callCount += 1;
      return new Response(JSON.stringify({ error: { message: 'Service unavailable' } }), {
        status: 500,
        headers: { 'Content-Type': 'application/json' },
      });
    };

    const provider = createOpenAIEmbeddingProvider(dummyConfig, { fetch: customFetch });

    await expect(
      provider.embed(['Văn bản test retry'], new AbortController().signal),
    ).rejects.toMatchObject({
      status: 500,
      retryable: true,
    });
    // Đảm bảo không có lần retry ngầm nào trong nội bộ SDK.
    expect(callCount).toBe(1);
  });

  it('làm sạch mã lỗi và bảo đảm không rò rỉ khóa OpenAI trong thông điệp lỗi', async () => {
    const customFetch: typeof fetch = async () => {
      return new Response(
        JSON.stringify({
          error: { message: 'Incorrect API key secret_openai_token provided' },
        }),
        {
          status: 401,
          headers: { 'Content-Type': 'application/json' },
        },
      );
    };

    const provider = createOpenAIEmbeddingProvider(dummyConfig, { fetch: customFetch });

    let caughtError: unknown;
    try {
      await provider.embed(['Thử lỗi xác thực'], new AbortController().signal);
    } catch (err) {
      caughtError = err;
    }

    expect(caughtError).toBeInstanceOf(ProviderError);
    if (caughtError instanceof ProviderError) {
      expect(caughtError.status).toBe(401);
      expect(caughtError.retryable).toBe(false);
      // Khóa API không bao giờ xuất hiện trong thông điệp lỗi trả ra.
      expect(caughtError.message).not.toContain('secret_openai_token');
    }
  });
});
