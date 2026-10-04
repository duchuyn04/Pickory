import { AppError } from '../../shared/errors';

interface ThrottleRecord {
  attempts: number;
  lockedUntil: number;
  resetAt: number;
}

const WINDOW_MS = 15 * 60 * 1000; // 15 phút
const MAX_ATTEMPTS = 5;

class LoginThrottle {
  private readonly records = new Map<string, ThrottleRecord>();

  private getKey(email: string, ip: string): string {
    return `${email.trim().toLowerCase()}|${ip.trim()}`;
  }

  /**
   * Kiểm tra xem cặp (email, ip) có đang bị khóa hay không.
   * Nếu đang bị khóa thì ném lỗi 429.
   */
  assertNotLocked(email: string, ip: string, now = Date.now()): void {
    const key = this.getKey(email, ip);
    const record = this.records.get(key);
    if (!record) {
      return;
    }

    if (now >= record.resetAt) {
      this.records.delete(key);
      return;
    }

    if (record.lockedUntil > now) {
      throw new AppError(
        'TOO_MANY_ATTEMPTS',
        429,
        'Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau 15 phút.',
      );
    }
  }

  /**
   * Ghi nhận 1 lần đăng nhập thất bại.
   * Lần thứ 5 vẫn chưa khóa ngay lúc gửi, nhưng lần thứ 6 sẽ bị chặn bởi `assertNotLocked`.
   */
  recordFailure(email: string, ip: string, now = Date.now()): void {
    const key = this.getKey(email, ip);
    const record = this.records.get(key);

    if (!record || now >= record.resetAt) {
      this.records.set(key, {
        attempts: 1,
        lockedUntil: 0,
        resetAt: now + WINDOW_MS,
      });
      return;
    }

    record.attempts += 1;
    if (record.attempts >= MAX_ATTEMPTS) {
      record.lockedUntil = now + WINDOW_MS;
      record.resetAt = now + WINDOW_MS;
    }
  }

  /**
   * Xóa bộ đếm khi đăng nhập thành công.
   */
  recordSuccess(email: string, ip: string): void {
    const key = this.getKey(email, ip);
    this.records.delete(key);
  }

  /**
   * Dọn sạch dữ liệu (dùng cho unit test).
   */
  clear(): void {
    this.records.clear();
  }
}

export const loginThrottle = new LoginThrottle();
