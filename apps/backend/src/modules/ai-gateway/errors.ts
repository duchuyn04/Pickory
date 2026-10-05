import { AppError } from '../../shared/errors';

export class AiUnavailableError extends AppError {
  constructor(public readonly reasonCode = 'AI_UNAVAILABLE') {
    // Mã lỗi ổn định cho phép module gọi chọn phương án dự phòng mà không lộ lỗi SDK.
    super('AI_UNAVAILABLE', 503, 'Dịch vụ AI tạm thời không khả dụng, vui lòng thử lại sau.');
  }
}
export class AiBudgetExceededError extends AppError {
  constructor(message = 'Đã đạt giới hạn sử dụng AI, vui lòng thử lại sau.') {
    // Dùng 429 vì yêu cầu bị chặn bởi hạn mức, không phải lỗi nội dung gửi tới provider.
    super('AI_BUDGET_EXCEEDED', 429, message);
  }
}
export class AiInvalidOutputError extends AppError {
  constructor() {
    // Dữ liệu provider sai schema là lỗi upstream nên trả 502 thay vì lỗi nhập liệu 400.
    super('AI_INVALID_OUTPUT', 502, 'Dịch vụ AI trả về dữ liệu không hợp lệ.');
  }
}
export class ProviderError extends Error {
  constructor(
    public readonly status?: number,
    public readonly retryable = false,
    public readonly code = status === undefined
      ? 'PROVIDER_NETWORK_ERROR'
      : `PROVIDER_HTTP_${status}`,
  ) {
    // Không giữ lỗi gốc vì SDK có thể đính kèm khóa hoặc nội dung request.
    super('AI provider request failed');
  }
}

export function getAiErrorCode(error: unknown): string {
  // Không dùng Error.name hoặc message vì nội dung này có thể do SDK hoặc dữ liệu ngoài kiểm soát.
  if (error instanceof AiUnavailableError) return error.reasonCode;
  if (error instanceof AppError) return error.code;
  if (error instanceof ProviderError) return error.code;
  return 'AI_UNEXPECTED_ERROR';
}
