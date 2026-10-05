import { createOpenAI } from '@ai-sdk/openai';
import { embedMany } from 'ai';
import type { Config } from '../../../shared/config';
import { ProviderError } from '../errors';
import type { EmbeddingProvider, TokenUsage } from '../types';
import { mapProviderError } from './error-mapper';

// Tùy chọn cấu hình cho OpenAI embedding provider hỗ trợ truyền hàm fetch tùy biến cho kiểm thử.
export type OpenAIEmbeddingProviderOptions = {
  fetch?: typeof fetch;
};

// Khởi tạo adapter OpenAI embedding tuân thủ hợp đồng EmbeddingProvider.
export function createOpenAIEmbeddingProvider(
  config: Config,
  options: OpenAIEmbeddingProviderOptions = {},
): EmbeddingProvider {
  // Bắt buộc cấu hình tên model embedding tường minh; không dùng model mặc định ngầm khi rỗng.
  const modelName = config.EMBEDDING_MODEL.trim();
  if (!modelName) {
    throw new ProviderError(400, false);
  }
  // Khởi tạo OpenAI provider của AI SDK với khóa xác thực và fetch tiêm phụ thuộc nếu có.
  const openai = createOpenAI({
    apiKey: config.EMBEDDING_API_KEY,
    fetch: options.fetch,
  });

  // Tạo thể hiện embedding model tương ứng với mã model cấu hình.
  const embeddingModel = openai.embedding(modelName);

  return {
    name: 'openai',
    model: modelName,

    async embed(
      texts: string[],
      signal: AbortSignal,
    ): Promise<{ vectors: number[][]; usage: TokenUsage }> {
      // Trả về mảng rỗng và usage bằng 0 ngay nếu danh sách đầu vào không có phần tử nào.
      if (texts.length === 0) {
        return {
          vectors: [],
          usage: {
            inputTokens: 0,
            outputTokens: 0,
            cachedTokens: 0,
          },
        };
      }

      try {
        // Gọi embedMany từ AI SDK với maxRetries bằng 0 để bộ bọc Reliability kiểm soát retry.
        const result = await embedMany({
          model: embeddingModel,
          values: texts,
          abortSignal: signal,
          maxRetries: 0,
          telemetry: { isEnabled: false, recordInputs: false, recordOutputs: false },
          // Chỉ định tường minh kích thước vector 1536 chiều qua tùy chọn của OpenAI provider.
          providerOptions: {
            openai: {
              dimensions: 1536,
            },
          },
        });

        const vectors = result.embeddings;

        // Kiểm tra danh sách vector trả về phải là mảng khớp đúng số lượng văn bản đầu vào.
        if (!Array.isArray(vectors) || vectors.length !== texts.length) {
          throw new ProviderError(undefined, false);
        }

        // Kiểm tra từng vector phải đủ 1536 chiều và mọi phần tử đều là số hữu hạn (finite).
        for (const vector of vectors) {
          // Bắt buộc vector phải đúng 1536 chiều để phù hợp định dạng cột vector(1536) của pgvector.
          if (!Array.isArray(vector) || vector.length !== 1536) {
            throw new ProviderError(undefined, false);
          }
          // Kiểm tra tính hợp lệ của từng tọa độ, loại bỏ hoàn toàn NaN, Infinity hoặc dữ liệu không phải số.
          for (const coordinate of vector) {
            if (typeof coordinate !== 'number' || !Number.isFinite(coordinate)) {
              throw new ProviderError(undefined, false);
            }
          }
        }

        // Trích xuất số lượng token đầu vào tiêu thụ cho lượt tạo embedding này.
        const totalTokens = result.usage?.tokens ?? 0;
        return {
          vectors,
          usage: {
            inputTokens: totalTokens,
            outputTokens: 0,
            cachedTokens: 0,
          },
        };
      } catch (error) {
        // Ánh xạ mọi lỗi phát sinh sang ProviderError chuẩn hóa để che giấu khóa và thông tin nhạy cảm.
        throw mapProviderError(error);
      }
    },
  };
}
