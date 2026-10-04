import { AppError } from '../../shared/errors';
import { loginThrottle } from './login-throttle';

interface RateLimitRecord {
  attempts: number;
  // Thời điểm cửa sổ đếm kết thúc.
  resetAt: number;
}

// Giới hạn số lần gọi theo khóa bất kỳ (IP, email) trong một khoảng thời gian; nằm trong bộ nhớ nên chỉ đúng khi chạy 1 instance.
export class RateLimiter {
  private readonly records = new Map<string, RateLimitRecord>();

  constructor(
    private readonly windowMs: number,
    private readonly maxAttempts: number,
    private readonly errorMessage: string = 'Quá nhiều yêu cầu. Vui lòng thử lại sau.',
  ) {}

  // Ném 429 nếu khóa đã dùng hết số lần cho phép trong cửa sổ hiện tại.
  assertNotRateLimited(key: string, now = Date.now()): void {
    const record = this.records.get(key);
    // Khóa chưa từng gọi thì cho qua.
    if (!record) {
      return;
    }

    // Cửa sổ đã hết thì xóa bản ghi, vừa cho gọi lại vừa giải phóng bộ nhớ.
    if (now >= record.resetAt) {
      this.records.delete(key);
      return;
    }

    // Đã chạm ngưỡng thì chặn.
    if (record.attempts >= this.maxAttempts) {
      throw new AppError('TOO_MANY_REQUESTS', 429, this.errorMessage);
    }
  }

  // Tính thêm một lần gọi cho khóa.
  recordAttempt(key: string, now = Date.now()): void {
    const record = this.records.get(key);
    // Khóa mới hoặc cửa sổ cũ đã hết: bắt đầu cửa sổ mới.
    if (!record || now >= record.resetAt) {
      this.records.set(key, {
        attempts: 1,
        resetAt: now + this.windowMs,
      });
      return;
    }

    record.attempts += 1;
  }

  // Giống assertNotRateLimited nhưng trả boolean thay vì ném lỗi.
  // Dùng cho forgot-password: vượt ngưỡng vẫn phải trả response như bình thường, nếu không sẽ lộ email nào bị gọi nhiều.
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

  // Kiểm tra rồi mới tính lượt, để request bị chặn không tự kéo dài thời gian bị chặn.
  consume(key: string, now = Date.now()): void {
    this.assertNotRateLimited(key, now);
    this.recordAttempt(key, now);
  }

  // Xóa một khóa, hoặc xóa tất cả nếu không truyền khóa.
  clear(key?: string): void {
    if (key) {
      this.records.delete(key);
    } else {
      this.records.clear();
    }
  }
}

// Đăng ký: 5 lần mỗi giờ mỗi IP, để bot không tạo hàng loạt tài khoản rác và không ép máy chủ chạy Argon2 liên tục.
export const registerRateLimiter = new RateLimiter(
  60 * 60 * 1000,
  5,
  'Bạn đã đăng ký quá nhiều lần từ địa chỉ này. Vui lòng thử lại sau 1 giờ.',
);

// Quên mật khẩu theo email: 3 lần mỗi 15 phút, để kẻ xấu không dùng hệ thống spam hòm thư của nạn nhân.
export const forgotPasswordEmailLimiter = new RateLimiter(
  15 * 60 * 1000,
  3,
  'Yêu cầu đặt lại mật khẩu quá thường xuyên cho email này.',
);

// Quên mật khẩu theo IP: 10 lần mỗi giờ, để một máy không quét hàng loạt email dù đổi email từng lần.
export const forgotPasswordIpLimiter = new RateLimiter(
  60 * 60 * 1000,
  10,
  'Yêu cầu đặt lại mật khẩu quá thường xuyên từ IP này.',
);

// Dùng trong test để các test không ảnh hưởng nhau; gồm cả bộ đếm đăng nhập vì chúng cùng dựa vào IP.
export function clearAllRateLimits(): void {
  registerRateLimiter.clear();
  forgotPasswordEmailLimiter.clear();
  forgotPasswordIpLimiter.clear();
  loginThrottle.clear();
}
