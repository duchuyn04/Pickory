import { describe, expect, it } from 'vitest';
import { AiBudgetExceededError } from '../../../../src/modules/ai-gateway/errors';
import { ChatRateLimiter } from '../../../../src/modules/ai-gateway/rate-limit';

describe('Quản lý hạn mức trò chuyện ChatRateLimiter', () => {
  it('chặn ngay khi actorKey thiếu hoặc sai tiền tố', () => {
    const limiter = new ChatRateLimiter();

    // Thiếu actorKey phải báo lỗi tham số sớm.
    expect(() => limiter.checkAndIncrement(undefined, 50, 15)).toThrow('actorKey là bắt buộc');
    expect(() => limiter.checkAndIncrement('', 50, 15)).toThrow('actorKey là bắt buộc');

    // Sai định dạng tiền tố user: hoặc anon:.
    expect(() => limiter.checkAndIncrement('invalid_key_123', 50, 15)).toThrow(
      'actorKey sai định dạng',
    );
    expect(() => limiter.checkAndIncrement('user:', 50, 15)).toThrow(
      'định danh sau tiền tố không được để trống',
    );
    expect(() => limiter.checkAndIncrement('anon:', 50, 15)).toThrow(
      'định danh sau tiền tố không được để trống',
    );
  });

  it('chặn khách vãng lai ở lượt gọi thứ 16 trong ngày', () => {
    const limiter = new ChatRateLimiter();
    const anonKey = 'anon:guest_device_999';

    // Gọi đủ 15 lần cho phép.
    for (let count = 1; count <= 15; count += 1) {
      limiter.checkAndIncrement(anonKey, 50, 15);
    }

    // Lần thứ 16 phải bị từ chối với thông báo chứa đúng hạn mức 15.
    expect(() => limiter.checkAndIncrement(anonKey, 50, 15)).toThrow(AiBudgetExceededError);
    expect(() => limiter.checkAndIncrement(anonKey, 50, 15)).toThrow('15 lượt trò chuyện miễn phí');
  });

  it('phản ánh hạn mức tùy chỉnh từ cấu hình admin trong thông báo lỗi', () => {
    const limiter = new ChatRateLimiter();
    const anonKey = 'anon:custom_limit_guest';

    // Admin cấu hình hạn mức 5 lượt thay vì 15.
    for (let count = 1; count <= 5; count += 1) {
      limiter.checkAndIncrement(anonKey, 80, 5);
    }

    // Lần thứ 6 phải báo rõ con số 5 lượt thay vì con số cứng 15.
    expect(() => limiter.checkAndIncrement(anonKey, 80, 5)).toThrow('5 lượt trò chuyện miễn phí');
  });

  it('chặn người dùng đã đăng nhập ở lượt gọi thứ 51 trong ngày', () => {
    const limiter = new ChatRateLimiter();
    const userKey = 'user:member_account_123';

    // Gọi đủ 50 lần.
    for (let count = 1; count <= 50; count += 1) {
      limiter.checkAndIncrement(userKey, 50, 15);
    }

    // Lần thứ 51 phải ném lỗi vượt hạn mức người dùng.
    expect(() => limiter.checkAndIncrement(userKey, 50, 15)).toThrow(AiBudgetExceededError);
    expect(() => limiter.checkAndIncrement(userKey, 50, 15)).toThrow(
      '50 lượt trò chuyện trong ngày',
    );
  });

  it('tự động đặt lại bộ đếm khi bước sang ngày mới theo giờ Việt Nam', () => {
    // Ngày 2026-10-05 23:59:00 tại Việt Nam (UTC: 16:59:00).
    const timeAtEndOfDay = new Date('2026-10-05T16:59:00.000Z').getTime();
    let simulatedTime = timeAtEndOfDay;

    const limiter = new ChatRateLimiter(() => simulatedTime);
    const guestKey = 'anon:visitor_phone_test';

    // Dùng hết 15 lượt trong ngày 05/10.
    for (let i = 0; i < 15; i += 1) {
      limiter.checkAndIncrement(guestKey, 50, 15);
    }
    expect(() => limiter.checkAndIncrement(guestKey, 50, 15)).toThrow(AiBudgetExceededError);

    // Chuyển sang ngày 2026-10-06 00:01:00 tại Việt Nam (UTC: 17:01:00 ngày 05/10).
    simulatedTime = new Date('2026-10-05T17:01:00.000Z').getTime();

    // Bước sang ngày mới theo múi giờ Việt Nam, khách có lại lượt trò chuyện.
    expect(() => limiter.checkAndIncrement(guestKey, 50, 15)).not.toThrow();
  });
});
