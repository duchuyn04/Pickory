import { describe, expect, it } from 'vitest';
import { ProviderError } from '../../../../../src/modules/ai-gateway/errors';
import { mapProviderError } from '../../../../../src/modules/ai-gateway/providers/error-mapper';

describe('Bộ ánh xạ lỗi nhà cung cấp (mapProviderError)', () => {
  it('giữ nguyên thể hiện nếu lỗi đã là ProviderError', () => {
    // Không bọc lại nếu đối tượng lỗi đã được chuẩn hóa trước đó.
    const original = new ProviderError(429, true);
    const mapped = mapProviderError(original);
    expect(mapped).toBe(original);
    expect(mapped.status).toBe(429);
    expect(mapped.retryable).toBe(true);
  });

  it('ánh xạ lỗi HTTP mã 429 thành lỗi retryable', () => {
    // 429 là lỗi quá tải tần suất, bắt buộc phải retry theo đặc tả AI-03.
    const apiError = Object.assign(new Error('Rate limit exceeded with key secret_key_123'), {
      statusCode: 429,
      url: 'https://api.deepseek.com/chat/completions',
    });
    const mapped = mapProviderError(apiError);
    expect(mapped).toBeInstanceOf(ProviderError);
    expect(mapped.status).toBe(429);
    expect(mapped.retryable).toBe(true);
    // Tuyệt đối không giữ lại chuỗi lỗi gốc để chống rò rỉ secret key hoặc URL.
    expect(mapped.message).not.toContain('secret_key_123');
    expect(mapped.message).not.toContain('https://api.deepseek.com');
  });

  it('ánh xạ lỗi HTTP mã 5xx thành lỗi retryable', () => {
    // 500 và 503 là lỗi máy chủ tạm thời, cho phép thử lại tối đa hai lần.
    const serverError = Object.assign(new Error('Internal server error'), {
      statusCode: 503,
    });
    const mapped = mapProviderError(serverError);
    expect(mapped.status).toBe(503);
    expect(mapped.retryable).toBe(true);
  });

  it('ánh xạ lỗi HTTP mã 4xx khác thành lỗi không retry', () => {
    // 400, 401 hoặc 403 xuất phát từ client hoặc xác thực, không retry.
    const clientError = Object.assign(new Error('Unauthorized access'), {
      statusCode: 401,
    });
    const mapped = mapProviderError(clientError);
    expect(mapped.status).toBe(401);
    expect(mapped.retryable).toBe(false);
  });
  it('ánh xạ các lỗi cấu hình SDK không thể retry thành mã 400', () => {
    // Sai tên model hoặc thiếu khóa API không thể giải quyết bằng cách thử lại.
    const modelError = new Error('Model not found');
    modelError.name = 'AI_NoSuchModelError';
    const mappedModel = mapProviderError(modelError);
    expect(mappedModel.status).toBe(400);
    expect(mappedModel.retryable).toBe(false);

    const keyError = new Error('API key missing');
    keyError.name = 'AI_LoadAPIKeyError';
    const mappedKey = mapProviderError(keyError);
    expect(mappedKey.status).toBe(400);
    expect(mappedKey.retryable).toBe(false);
  });

  it('trích xuất mã trạng thái từ đối tượng lỗi lồng nhau trong cause', () => {
    // Lỗi bọc qua nhiều lớp vẫn phải lấy được status HTTP gốc.
    const wrappedError = {
      name: 'CustomError',
      cause: { statusCode: 502 },
    };
    const mapped = mapProviderError(wrappedError);
    expect(mapped.status).toBe(502);
    expect(mapped.retryable).toBe(true);
  });

  it('ánh xạ lỗi mạng hoặc abort không có mã HTTP thành lỗi retryable', () => {
    // Mất kết nối mạng hoặc timeout phải cho phép retry theo chính sách độ tin cậy.
    const networkError = new TypeError('fetch failed');
    const mapped = mapProviderError(networkError);
    expect(mapped.status).toBeUndefined();
    expect(mapped.retryable).toBe(true);

    const abortError = Object.assign(new Error('The operation was aborted'), {
      name: 'AbortError',
    });
    expect(mapProviderError(abortError).retryable).toBe(true);

    const connRefused = Object.assign(new Error('connect ECONNREFUSED'), { code: 'ECONNREFUSED' });
    expect(mapProviderError(connRefused).retryable).toBe(true);
  });

  it('đánh dấu lỗi lập trình hoặc parse cú pháp không có mã HTTP là không retry', () => {
    // Lỗi cú pháp hoặc logic code nội bộ không thể giải quyết bằng cách thử lại.
    const syntaxError = new SyntaxError('Unexpected token in JSON');
    const mapped = mapProviderError(syntaxError);
    expect(mapped.status).toBeUndefined();
    expect(mapped.retryable).toBe(false);
  });
});
