import { AppError } from '../../../shared/errors';

interface ThrottleRecord {
  // Số lần sai trong cửa sổ hiện tại.
  attempts: number;
  // Thời điểm hết khóa; 0 nghĩa là chưa bị khóa.
  lockedUntil: number;
  // Thời điểm cửa sổ đếm kết thúc, sau đó bộ đếm bắt đầu lại.
  resetAt: number;
}

// Cửa sổ đếm và thời gian khóa cùng là 15 phút theo spec.
const WINDOW_MS = 15 * 60 * 1000;
// Sai 5 lần thì lần thứ 6 bị chặn.
const MAX_ATTEMPTS = 5;

// Bộ đếm đăng nhập sai nằm trong bộ nhớ, đủ dùng khi chỉ chạy 1 instance API.
class LoginThrottle {
  private readonly records = new Map<string, ThrottleRecord>();

  private getKey(email: string, ip: string): string {
    // Đếm theo cặp (email, IP): kẻ tấn công không khóa được tài khoản người khác từ IP lạ, và một IP thử nhiều email vẫn bị đếm từng email.
    // Chuẩn hóa chữ thường để Abc@x.com và abc@x.com dùng chung một bộ đếm.
    return `${email.trim().toLowerCase()}|${ip.trim()}`;
  }

  // Gọi trước khi kiểm tra mật khẩu để chặn ngay cả khi mật khẩu nhập vào đúng.
  assertNotLocked(email: string, ip: string, now = Date.now()): void {
    const key = this.getKey(email, ip);
    const record = this.records.get(key);
    // Chưa từng sai thì cho qua.
    if (!record) {
      return;
    }

    // Cửa sổ đã hết thì xóa bản ghi cũ, vừa cho thử lại vừa không để Map phình mãi.
    if (now >= record.resetAt) {
      this.records.delete(key);
      return;
    }

    // Còn trong thời gian khóa thì từ chối, không kiểm tra mật khẩu để không tốn CPU Argon2.
    if (record.lockedUntil > now) {
      throw new AppError(
        'TOO_MANY_ATTEMPTS',
        429,
        'Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau 15 phút.',
      );
    }
  }

  // Gọi sau mỗi lần đăng nhập sai.
  recordFailure(email: string, ip: string, now = Date.now()): void {
    const key = this.getKey(email, ip);
    const record = this.records.get(key);

    // Lần sai đầu tiên, hoặc cửa sổ cũ đã hết: bắt đầu đếm lại từ 1.
    if (!record || now >= record.resetAt) {
      this.records.set(key, {
        attempts: 1,
        lockedUntil: 0,
        resetAt: now + WINDOW_MS,
      });
      return;
    }

    record.attempts += 1;
    // Chạm ngưỡng thì khóa; lần sai thứ 5 vẫn trả 401, đến lần thử thứ 6 mới nhận 429.
    if (record.attempts >= MAX_ATTEMPTS) {
      record.lockedUntil = now + WINDOW_MS;
      // Dời resetAt theo để bản ghi không bị xóa trước khi hết khóa.
      record.resetAt = now + WINDOW_MS;
    }
  }

  // Đăng nhập đúng thì xóa bộ đếm, để lần gõ sai tình cờ trước đó không cộng dồn vào lần sau.
  recordSuccess(email: string, ip: string): void {
    const key = this.getKey(email, ip);
    this.records.delete(key);
  }

  // Dùng trong test để các test không ảnh hưởng nhau.
  clear(): void {
    this.records.clear();
  }
}

export const loginThrottle = new LoginThrottle();
