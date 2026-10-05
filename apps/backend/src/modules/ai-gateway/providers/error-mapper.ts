import { APICallError } from 'ai';
import { ProviderError } from '../errors';

// Bảng tra cứu tĩnh các tên lỗi cấu hình hoặc tham số không thể tự phục hồi qua retry.
const NON_RETRYABLE_SDK_ERROR_NAMES: Record<string, true> = {
  AI_NoSuchModelError: true,
  AI_LoadAPIKeyError: true,
  AI_InvalidPromptError: true,
  AI_InvalidArgumentError: true,
  AI_TypeValidationError: true,
  AI_UnsupportedFunctionalityError: true,
};

// Xác định lỗi mạng, timeout hoặc abort thực sự để chỉ retry đúng các sự cố tạm thời.
function isNetworkError(error: unknown): boolean {
  if (!error || typeof error !== 'object') {
    return false;
  }
  if ('name' in error && (error.name === 'AbortError' || error.name === 'TimeoutError')) {
    return true;
  }
  if ('code' in error && typeof error.code === 'string') {
    const code = error.code;
    if (
      code === 'ECONNREFUSED' ||
      code === 'ECONNRESET' ||
      code === 'ETIMEDOUT' ||
      code === 'ENOTFOUND' ||
      code === 'EAI_AGAIN'
    ) {
      return true;
    }
  }
  if (error instanceof TypeError && error.message.toLowerCase().includes('fetch')) {
    return true;
  }
  if (APICallError.isInstance(error) && (error.statusCode === undefined || error.isRetryable)) {
    return true;
  }
  if ('cause' in error && error.cause) {
    return isNetworkError(error.cause);
  }
  return false;
}
// Ánh xạ mọi ngoại lệ phát sinh từ AI SDK hoặc HTTP fetch thành ProviderError an toàn.
// Tuyệt đối không giữ lại thông điệp gốc, URL request hay khóa xác thực để chống rò rỉ.
export function mapProviderError(error: unknown): ProviderError {
  // Nếu lỗi đã được bọc thành ProviderError từ trước thì tái sử dụng nguyên vẹn.
  if (error instanceof ProviderError) {
    return error;
  }

  // Nhận diện lỗi từ AI SDK để trích xuất mã trạng thái HTTP nếu có.
  if (APICallError.isInstance(error)) {
    const statusCode = error.statusCode;
    // Mã 429 biểu thị vượt giới hạn tần suất gọi từ phía nhà cung cấp, bắt buộc retry.
    if (statusCode === 429) {
      return new ProviderError(429, true);
    }
    // Mã 5xx là sự cố máy chủ tạm thời từ nhà cung cấp, cho phép thử lại.
    if (statusCode !== undefined && statusCode >= 500 && statusCode < 600) {
      return new ProviderError(statusCode, true);
    }
    // Các mã 4xx khác (như 400, 401, 403, 404, 422) là lỗi request, không retry.
    if (statusCode !== undefined && statusCode >= 400 && statusCode < 500) {
      return new ProviderError(statusCode, false);
    }
    // Trường hợp có mã trạng thái khác không thuộc nhóm lỗi máy chủ tạm thời.
    if (statusCode !== undefined) {
      return new ProviderError(statusCode, false);
    }
  }

  // Kiểm tra tên định danh lỗi để chặn thử lại các lỗi lập trình hoặc sai cấu hình model.
  if (error && typeof error === 'object' && 'name' in error && typeof error.name === 'string') {
    // Lỗi cấu hình model hoặc khóa API thiếu không thể thành công khi retry.
    if (NON_RETRYABLE_SDK_ERROR_NAMES[error.name]) {
      return new ProviderError(400, false);
    }
  }

  // Trích xuất mã trạng thái HTTP từ các đối tượng phản hồi lỗi tổng quát hoặc nguyên nhân lồng nhau.
  let extractedStatus: number | undefined;
  if (error && typeof error === 'object') {
    if ('statusCode' in error && typeof error.statusCode === 'number') {
      extractedStatus = error.statusCode;
    } else if ('status' in error && typeof error.status === 'number') {
      extractedStatus = error.status;
    } else if ('cause' in error && error.cause && typeof error.cause === 'object') {
      const causeObj = error.cause;
      if ('statusCode' in causeObj && typeof causeObj.statusCode === 'number') {
        extractedStatus = causeObj.statusCode;
      } else if ('status' in causeObj && typeof causeObj.status === 'number') {
        extractedStatus = causeObj.status;
      }
    }
  }

  // Áp dụng quy tắc retry dựa trên mã trạng thái trích xuất được.
  if (extractedStatus !== undefined) {
    // 429 và 5xx là các lỗi tạm thời được phép retry theo đặc tả AI-03.
    const isTransient =
      extractedStatus === 429 || (extractedStatus >= 500 && extractedStatus < 600);
    return new ProviderError(extractedStatus, isTransient);
  }

  // Với lỗi không có mã HTTP, chỉ cho phép retry nếu nhận diện đúng là lỗi mạng, timeout hoặc abort;
  // các lỗi lập trình, cú pháp hoặc parse dữ liệu phải đánh dấu không retry (retryable: false).
  const retryable = isNetworkError(error);
  return new ProviderError(undefined, retryable);
}
