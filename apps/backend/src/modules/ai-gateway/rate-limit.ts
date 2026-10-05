import { AppError } from '../../shared/errors';
import { AiBudgetExceededError } from './errors';

const VIETNAM_OFFSET_HOURS = 7;
const VIETNAM_OFFSET_MILLISECONDS = VIETNAM_OFFSET_HOURS * 60 * 60 * 1000;

// Bộ quản lý hạn mức trò chuyện hàng ngày theo người dùng trong bộ nhớ.
export class ChatRateLimiter {
  private readonly counters = new Map<string, number>();
  private currentDay = '';

  constructor(private readonly now: () => number = Date.now) {}

  // Xóa toàn bộ bộ đếm phục vụ môi trường kiểm thử.
  reset(): void {
    this.counters.clear();
    this.currentDay = '';
  }

  // Chuyển đổi mốc thời gian hiện tại thành chuỗi ngày YYYY-MM-DD theo giờ Việt Nam.
  private getVietnamDayString(): string {
    const vietnamTime = new Date(this.now() + VIETNAM_OFFSET_MILLISECONDS);
    const year = vietnamTime.getUTCFullYear();
    const month = String(vietnamTime.getUTCMonth() + 1).padStart(2, '0');
    const day = String(vietnamTime.getUTCDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  // Kiểm tra tính hợp lệ của actorKey và tăng bộ đếm lượt gọi nếu chưa vượt hạn mức.
  checkAndIncrement(actorKey: string | undefined, userLimit: number, anonLimit: number): string {
    // Bắt buộc phải có actorKey để xác định định danh người dùng hoặc khách vãng lai.
    if (!actorKey || typeof actorKey !== 'string') {
      throw new AppError(
        'INVALID_ACTOR_KEY',
        400,
        'actorKey là bắt buộc cho tính năng trò chuyện và phải có tiền tố user: hoặc anon:.',
      );
    }

    const trimmedKey = actorKey.trim();
    const isUser = trimmedKey.startsWith('user:');
    const isAnon = trimmedKey.startsWith('anon:');

    // Chặn ngay các actorKey sai cấu trúc quy ước để phát hiện lỗi lập trình sớm.
    if (!isUser && !isAnon) {
      throw new AppError(
        'INVALID_ACTOR_KEY',
        400,
        'actorKey sai định dạng: phải bắt đầu bằng "user:" cho tài khoản hoặc "anon:" cho khách vãng lai.',
      );
    }

    // Đảm bảo phần định danh sau tiền tố không được để trống.
    const identifierPart = trimmedKey.slice(trimmedKey.indexOf(':') + 1);
    if (!identifierPart.trim()) {
      throw new AppError(
        'INVALID_ACTOR_KEY',
        400,
        'actorKey không hợp lệ: định danh sau tiền tố không được để trống.',
      );
    }

    const today = this.getVietnamDayString();
    // Đặt lại bộ đếm khi sang ngày mới: dọn sạch một lần duy nhất với độ phức tạp O(1).
    if (today !== this.currentDay) {
      this.counters.clear();
      this.currentDay = today;
    }

    const currentCount = this.counters.get(trimmedKey) ?? 0;
    const limit = isUser ? userLimit : anonLimit;

    // Chặn người dùng nếu đã chạm hoặc vượt số lượt gọi cho phép trong ngày theo cấu hình động.
    if (currentCount >= limit) {
      if (isAnon) {
        throw new AiBudgetExceededError(
          `Bạn đã dùng hết ${limit} lượt trò chuyện miễn phí trong ngày. Vui lòng đăng nhập hoặc quay lại vào ngày mai.`,
        );
      }
      throw new AiBudgetExceededError(
        `Bạn đã đạt giới hạn ${limit} lượt trò chuyện trong ngày. Vui lòng quay lại vào ngày mai.`,
      );
    }

    // Tăng bộ đếm một lần cho toàn bộ lời gọi logic.
    this.counters.set(trimmedKey, currentCount + 1);
    return today;
  }

  refund(actorKey: string, day: string): void {
    // Lỗi của lượt hôm trước không được trừ nhầm lượt mới sau khi bộ đếm đã chuyển ngày.
    if (day !== this.currentDay) return;
    const key = actorKey.trim();
    const count = this.counters.get(key) ?? 0;
    if (count <= 1) this.counters.delete(key);
    else this.counters.set(key, count - 1);
  }
}
