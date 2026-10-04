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
   * Chặn yêu cầu đăng nhập nếu cặp email và IP đang trong thời gian phạt do nhập sai quá số lần.
   */
  assertNotLocked(email: string, ip: string, now = Date.now()): void {
    const key = this.getKey(email, ip);
    const record = this.records.get(key);
    if (!record) {
      return;
    }

    // Tự động giải phóng bộ nhớ khi bản ghi đã vượt quá khung thời gian theo dõi cửa sổ trượt.
    if (now >= record.resetAt) {
      this.records.delete(key);
      return;
    }

    // Ngăn chặn thử tiếp nếu thời điểm hiện tại vẫn nằm trong khoảng thời gian bị tạm khóa.
    if (record.lockedUntil > now) {
      throw new AppError(
        'TOO_MANY_ATTEMPTS',
        429,
        'Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau 15 phút.',
      );
    }
  }

  /**
   * Tăng bộ đếm số lần đăng nhập sai và kích hoạt khóa tạm thời nếu chạm ngưỡng tối đa.
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
    // Khi chạm ngưỡng thất bại cho phép, kích hoạt khóa tạm thời 15 phút để ngăn chặn tấn công tự động hóa.
    if (record.attempts >= MAX_ATTEMPTS) {
      record.lockedUntil = now + WINDOW_MS;
      record.resetAt = now + WINDOW_MS;
    }
  }

  /**
   * Đặt lại bộ đếm khi xác thực thành công để không ảnh hưởng đến các phiên đăng nhập tương lai.
   */
  recordSuccess(email: string, ip: string): void {
    const key = this.getKey(email, ip);
    this.records.delete(key);
  }

  /**
   * Xóa toàn bộ dữ liệu điều tiết trong bộ nhớ phục vụ cô lập trạng thái giữa các ca kiểm thử.
   */
  clear(): void {
    this.records.clear();
  }
}

export const loginThrottle = new LoginThrottle();
