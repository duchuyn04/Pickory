import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  clearAllRateLimits,
  forgotPasswordEmailLimiter,
  forgotPasswordIpLimiter,
  RateLimiter,
  registerRateLimiter,
} from '../../../../src/modules/identity/rate-limit';

describe('ID-17 rate-limit unit', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    clearAllRateLimits();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('bộ đếm chung: chạm ngưỡng ném 429, hết cửa sổ mở lại, khóa khác không ảnh hưởng', () => {
    const limiter = new RateLimiter(60 * 1000, 3, 'Vượt ngưỡng thử nghiệm.');
    const keyA = 'ip-a';
    const keyB = 'ip-b';

    // 3 lần đầu thành công
    for (let i = 1; i <= 3; i++) {
      expect(() => limiter.assertNotRateLimited(keyA)).not.toThrow();
      expect(limiter.isRateLimited(keyA)).toBe(false);
      limiter.recordAttempt(keyA);
    }

    // Lần thứ 4: ném lỗi 429 và isRateLimited trả true
    expect(limiter.isRateLimited(keyA)).toBe(true);
    expect(() => limiter.assertNotRateLimited(keyA)).toThrowError(
      expect.objectContaining({ status: 429, code: 'TOO_MANY_REQUESTS' }),
    );
    expect(() => limiter.consume(keyA)).toThrowError(
      expect.objectContaining({ status: 429, code: 'TOO_MANY_REQUESTS' }),
    );

    // Khóa B độc lập, không bị ảnh hưởng bởi khóa A
    expect(limiter.isRateLimited(keyB)).toBe(false);
    expect(() => limiter.consume(keyB)).not.toThrow();

    // Tiến thời gian qua 60 giây (hết cửa sổ thời gian)
    vi.advanceTimersByTime(60 * 1000 + 100);

    // Khóa A được mở lại bình thường
    expect(limiter.isRateLimited(keyA)).toBe(false);
    expect(() => limiter.consume(keyA)).not.toThrow();
  });

  it('registerRateLimiter: 5 lần đầu cho phép, lần thứ 6 bị chặn 429 với thông báo tiếng Việt', () => {
    const ip = '192.168.1.100';

    for (let i = 1; i <= 5; i++) {
      expect(() => registerRateLimiter.consume(ip)).not.toThrow();
    }

    expect(() => registerRateLimiter.consume(ip)).toThrowError(
      expect.objectContaining({
        status: 429,
        code: 'TOO_MANY_REQUESTS',
        message: expect.stringContaining('Bạn đã đăng ký quá nhiều lần từ địa chỉ này'),
      }),
    );

    // Hết 1 giờ thì được đăng ký lại
    vi.advanceTimersByTime(60 * 60 * 1000 + 1000);
    expect(() => registerRateLimiter.consume(ip)).not.toThrow();
  });

  it('forgotPasswordEmailLimiter: tối đa 3 lần / 15 phút / email', () => {
    const email = 'customer@pickory.local';

    for (let i = 1; i <= 3; i++) {
      expect(forgotPasswordEmailLimiter.isRateLimited(email)).toBe(false);
      forgotPasswordEmailLimiter.recordAttempt(email);
    }

    // Lần thứ 4 vượt ngưỡng
    expect(forgotPasswordEmailLimiter.isRateLimited(email)).toBe(true);

    // Sau 15 phút được phục hồi
    vi.advanceTimersByTime(15 * 60 * 1000 + 1000);
    expect(forgotPasswordEmailLimiter.isRateLimited(email)).toBe(false);
  });

  it('forgotPasswordIpLimiter: tối đa 10 lần / 1 giờ / IP', () => {
    const ip = '10.10.10.10';

    for (let i = 1; i <= 10; i++) {
      expect(forgotPasswordIpLimiter.isRateLimited(ip)).toBe(false);
      forgotPasswordIpLimiter.recordAttempt(ip);
    }

    // Lần thứ 11 vượt ngưỡng
    expect(forgotPasswordIpLimiter.isRateLimited(ip)).toBe(true);

    // Sau 1 giờ được phục hồi
    vi.advanceTimersByTime(60 * 60 * 1000 + 1000);
    expect(forgotPasswordIpLimiter.isRateLimited(ip)).toBe(false);
  });
});
