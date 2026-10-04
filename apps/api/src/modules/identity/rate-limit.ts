import { AppError } from '../../shared/errors';
import { loginThrottle } from './login-throttle';

interface RateLimitRecord {
  attempts: number;
  resetAt: number;
}

/**
 * Bộ đếm tần suất thao tác trong bộ nhớ theo cửa sổ thời gian trượt/đặt lại.
 * Tuân thủ quy ước không thêm dependency ngoài, hỗ trợ tham số `now` để kiểm thử giả lập thời gian.
 */
export class RateLimiter {
  private readonly records = new Map<string, RateLimitRecord>();

  constructor(
    private readonly windowMs: number,
    private readonly maxAttempts: number,
    private readonly errorMessage: string = 'Quá nhiều yêu cầu. Vui lòng thử lại sau.',
  ) {}

  /**
   * Kiểm tra xem khóa định danh đã vượt quá số lần cho phép trong cửa sổ hiện tại chưa.
   * Nếu đã vượt ngưỡng thì ném lỗi 429 TOO_MANY_REQUESTS với thông báo tiếng Việt.
   */
  assertNotRateLimited(key: string, now = Date.now()): void {
    const record = this.records.get(key);
    if (!record) {
      return;
    }

    if (now >= record.resetAt) {
      this.records.delete(key);
      return;
    }

    if (record.attempts >= this.maxAttempts) {
      throw new AppError('TOO_MANY_REQUESTS', 429, this.errorMessage);
    }
  }

  /**
   * Ghi nhận một lần gọi cho khóa định danh.
   * Khởi tạo bản ghi mới nếu khóa chưa tồn tại hoặc cửa sổ trước đó đã hết hạn.
   */
  recordAttempt(key: string, now = Date.now()): void {
    const record = this.records.get(key);
    if (!record || now >= record.resetAt) {
      this.records.set(key, {
        attempts: 1,
        resetAt: now + this.windowMs,
      });
      return;
    }

    record.attempts += 1;
  }

  /**
   * Kiểm tra trạng thái vượt ngưỡng mà không ném lỗi (trả về boolean).
   * Dùng cho forgot-password để nuốt lỗi và trả response chung, chống dò tài khoản.
   */
  isRateLimited(key: string, now = Date.now()): boolean {
    const record = this.records.get(key);
    if (!record) {
      return false;
    }

    if (now >= record.resetAt) {
      this.records.delete(key);
      return false;
    }

    return record.attempts >= this.maxAttempts;
  }

  /**
   * Kiểm tra và ghi nhận trong một bước. Ném 429 nếu đã chạm ngưỡng.
   */
  consume(key: string, now = Date.now()): void {
    this.assertNotRateLimited(key, now);
    this.recordAttempt(key, now);
  }

  /**
   * Xóa một khóa cụ thể hoặc toàn bộ bộ đếm.
   */
  clear(key?: string): void {
    if (key) {
      this.records.delete(key);
    } else {
      this.records.clear();
    }
  }
}

// Đăng ký khách hàng: tối đa 5 lần / giờ / IP để chống spam tài khoản và quá tải CPU do Argon2
export const registerRateLimiter = new RateLimiter(
  60 * 60 * 1000,
  5,
  'Bạn đã đăng ký quá nhiều lần từ địa chỉ này. Vui lòng thử lại sau 1 giờ.',
);

// Quên mật khẩu: tối đa 3 lần / 15 phút / email để tránh bị lạm dụng làm công cụ spam hòm thư người khác
export const forgotPasswordEmailLimiter = new RateLimiter(
  15 * 60 * 1000,
  3,
  'Yêu cầu đặt lại mật khẩu quá thường xuyên cho email này.',
);

// Quên mật khẩu: tối đa 10 lần / 1 giờ / IP để chống một máy quét hàng loạt email
export const forgotPasswordIpLimiter = new RateLimiter(
  60 * 60 * 1000,
  10,
  'Yêu cầu đặt lại mật khẩu quá thường xuyên từ IP này.',
);

/**
 * Xóa toàn bộ bộ đếm tần suất và đăng nhập (dùng giữa các test tự động để tránh ảnh hưởng chéo).
 */
export function clearAllRateLimits(): void {
  registerRateLimiter.clear();
  forgotPasswordEmailLimiter.clear();
  forgotPasswordIpLimiter.clear();
  loginThrottle.clear();
}
