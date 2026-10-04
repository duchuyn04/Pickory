import { AppError } from '../../shared/errors';
import { loginThrottle } from './login-throttle';

interface RateLimitRecord {
  attempts: number;
  resetAt: number;
}

/**
 * Bộ giới hạn tần suất trong bộ nhớ phục vụ kiểm soát lưu lượng mà không phụ thuộc vào hạ tầng bên ngoài.
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

    // Giải phóng bộ nhớ khi thời gian theo dõi của cửa sổ hiện tại đã trôi qua.
    if (now >= record.resetAt) {
      this.records.delete(key);
      return;
    }

    // Chặn yêu cầu ngay khi số lượt gọi đạt hoặc vượt quá ngưỡng tối đa cho phép.
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
    // Bắt đầu chu kỳ đếm mới nếu khóa chưa từng xuất hiện hoặc chu kỳ cũ đã kết thúc.
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

// Giới hạn 5 lần đăng ký mỗi giờ trên một IP nhằm ngăn chặn bot tạo tài khoản rác và gây cạn kiệt tài nguyên băm Argon2.
export const registerRateLimiter = new RateLimiter(
  60 * 60 * 1000,
  5,
  'Bạn đã đăng ký quá nhiều lần từ địa chỉ này. Vui lòng thử lại sau 1 giờ.',
);

// Giới hạn 3 lần quên mật khẩu mỗi 15 phút trên một email để ngăn kẻ xấu lợi dụng hệ thống nhằm gửi thư rác quấy rối nạn nhân.
export const forgotPasswordEmailLimiter = new RateLimiter(
  15 * 60 * 1000,
  3,
  'Yêu cầu đặt lại mật khẩu quá thường xuyên cho email này.',
);

// Giới hạn 10 lần quên mật khẩu mỗi giờ trên một IP nhằm ngăn chặn các công cụ tự động quét dò email hàng loạt.
export const forgotPasswordIpLimiter = new RateLimiter(
  60 * 60 * 1000,
  10,
  'Yêu cầu đặt lại mật khẩu quá thường xuyên từ IP này.',
);

/**
 * Đặt lại trạng thái toàn bộ bộ giới hạn tần suất phục vụ cô lập môi trường giữa các ca kiểm thử.
 */
export function clearAllRateLimits(): void {
  registerRateLimiter.clear();
  forgotPasswordEmailLimiter.clear();
  forgotPasswordIpLimiter.clear();
  loginThrottle.clear();
}
