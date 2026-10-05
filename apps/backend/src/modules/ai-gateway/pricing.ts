import type { TokenUsage } from './types';

// Bảng giá ước tính cao điểm bảo thủ cho DeepSeek Flash theo tài liệu và quyết định kiến trúc.
// Đơn vị tính theo USD trên mỗi một triệu token.
export const DEEPSEEK_FLASH_CONSERVATIVE_PEAK = {
  inputMissPerMillionUsd: 0.3,
  inputHitPerMillionUsd: 0.006,
  outputPerMillionUsd: 1.2,
} as const;

// Bảng giá chính thức cho DeepSeek Pro theo tài liệu https://api-docs.deepseek.com/quick_start/pricing
// Peak deepseek-v4-pro: miss 1.32 USD/M, hit 0.044 USD/M, output 3.96 USD/M.
export const DEEPSEEK_PRO_PEAK = {
  inputMissPerMillionUsd: 1.32,
  inputHitPerMillionUsd: 0.044,
  outputPerMillionUsd: 3.96,
} as const;

// Đơn giá OpenAI text-embedding-3-small được xác minh từ nguồn chính thức: 0.02 USD / 1 triệu token.
export const OPENAI_EMBEDDING_SMALL = {
  inputPerMillionUsd: 0.02,
} as const;

// Đơn giá OpenAI text-embedding-3-large từ nguồn chính thức: 0.13 USD / 1 triệu token (cấu hình 1536 chiều).
export const OPENAI_EMBEDDING_LARGE = {
  inputPerMillionUsd: 0.13,
} as const;

export type ModelPricing = {
  inputMissPerMillionUsd: number;
  inputHitPerMillionUsd: number;
  outputPerMillionUsd: number;
};

// Trả về bảng giá của model dựa trên định danh chính thức hiện hành, loại bỏ hoàn toàn các model cũ đã ngưng hỗ trợ.
export function getModelPricing(model: string): ModelPricing | null {
  const normalized = model.trim().toLowerCase();

  // Chỉ chấp nhận các model giả lập được định danh cụ thể phục vụ test, tránh vô tình miễn phí model lạ.
  if (normalized === 'fake-chat' || normalized === 'fake-embedding') {
    return {
      inputMissPerMillionUsd: 0,
      inputHitPerMillionUsd: 0,
      outputPerMillionUsd: 0,
    };
  }

  // Khớp chính xác ID model DeepSeek Flash chính thức và các phiên bản flash hợp lệ theo tài liệu.
  if (
    normalized === 'deepseek-flash' ||
    normalized === 'deepseek-v4-flash' ||
    normalized === 'deepseek-v4-flash-vision-exp'
  ) {
    return {
      inputMissPerMillionUsd: DEEPSEEK_FLASH_CONSERVATIVE_PEAK.inputMissPerMillionUsd,
      inputHitPerMillionUsd: DEEPSEEK_FLASH_CONSERVATIVE_PEAK.inputHitPerMillionUsd,
      outputPerMillionUsd: DEEPSEEK_FLASH_CONSERVATIVE_PEAK.outputPerMillionUsd,
    };
  }

  // Khớp chính xác ID model DeepSeek Pro ổn định chính thức deepseek-v4-pro.
  if (normalized === 'deepseek-v4-pro') {
    return {
      inputMissPerMillionUsd: DEEPSEEK_PRO_PEAK.inputMissPerMillionUsd,
      inputHitPerMillionUsd: DEEPSEEK_PRO_PEAK.inputHitPerMillionUsd,
      outputPerMillionUsd: DEEPSEEK_PRO_PEAK.outputPerMillionUsd,
    };
  }

  // Khớp chính xác ID model embedding chuẩn của OpenAI 1536 chiều.
  if (normalized === 'text-embedding-3-small') {
    return {
      inputMissPerMillionUsd: OPENAI_EMBEDDING_SMALL.inputPerMillionUsd,
      inputHitPerMillionUsd: OPENAI_EMBEDDING_SMALL.inputPerMillionUsd,
      outputPerMillionUsd: 0,
    };
  }

  // Khớp chính xác ID model embedding large của OpenAI 1536 chiều.
  if (normalized === 'text-embedding-3-large') {
    return {
      inputMissPerMillionUsd: OPENAI_EMBEDDING_LARGE.inputPerMillionUsd,
      inputHitPerMillionUsd: OPENAI_EMBEDDING_LARGE.inputPerMillionUsd,
      outputPerMillionUsd: 0,
    };
  }

  // Trả về null cho mọi ID model chưa được thẩm định để kích hoạt cơ chế thất bại an toàn (fail-safe).
  return null;
}

// Tính chi phí USD từ số lượng token thực tế và tên model được cấu hình.
export function calculateTokenCostUsd(model: string, usage: TokenUsage): number {
  const pricing = getModelPricing(model);

  // Dừng thực thi an toàn: từ chối tính phí bằng không nếu model chưa được đăng ký bảng giá rõ ràng.
  if (!pricing) {
    throw new Error(
      `Không thể tính chi phí cho model "${model}". Để bảo vệ ngân sách, hệ thống từ chối tính phí 0 USD cho model chưa rõ đơn giá.`,
    );
  }

  // Tách token nhận trực tiếp và token đọc từ cache prompt để tính đúng đơn giá từng loại.
  const cachedTokens = Math.max(0, usage.cachedTokens || 0);
  const nonCachedInputTokens = Math.max(0, (usage.inputTokens || 0) - cachedTokens);
  const outputTokens = Math.max(0, usage.outputTokens || 0);

  // Tính tổng chi phí theo tỉ lệ 1 triệu token để giữ độ chính xác số thực.
  const inputCost = (nonCachedInputTokens * pricing.inputMissPerMillionUsd) / 1_000_000;
  const cacheCost = (cachedTokens * pricing.inputHitPerMillionUsd) / 1_000_000;
  const outputCost = (outputTokens * pricing.outputPerMillionUsd) / 1_000_000;

  return inputCost + cacheCost + outputCost;
}
