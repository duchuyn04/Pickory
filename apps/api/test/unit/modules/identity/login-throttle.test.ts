import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { loginThrottle } from '../../../../src/modules/identity/login-throttle';

describe('ID-06 login throttle unit', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    loginThrottle.clear();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('lần sai thứ 5 vẫn chưa khóa; lần thứ 6 bị khóa 429', () => {
    const email = 'victim@example.com';
    const ip = '192.168.1.1';

    // 5 lần sai
    for (let i = 1; i <= 5; i++) {
      expect(() => loginThrottle.assertNotLocked(email, ip, Date.now())).not.toThrow();
      loginThrottle.recordFailure(email, ip, Date.now());
    }

    // Lần thứ 6: assertNotLocked ném 429
    expect(() => loginThrottle.assertNotLocked(email, ip, Date.now())).toThrowError(
      expect.objectContaining({ status: 429, code: 'TOO_MANY_ATTEMPTS' }),
    );
  });

  it('hết 15 phút thì thử lại được bình thường', () => {
    const email = 'user@example.com';
    const ip = '10.0.0.1';

    for (let i = 1; i <= 5; i++) {
      loginThrottle.recordFailure(email, ip, Date.now());
    }

    // Đang bị khóa
    expect(() => loginThrottle.assertNotLocked(email, ip, Date.now())).toThrow();

    // Tiến tới 15 phút 1 giây sau
    vi.advanceTimersByTime(15 * 60 * 1000 + 1000);

    // Không còn bị khóa
    expect(() => loginThrottle.assertNotLocked(email, ip, Date.now())).not.toThrow();
  });

  it('cặp (email, IP) khác không bị ảnh hưởng', () => {
    const email1 = 'user1@example.com';
    const email2 = 'user2@example.com';
    const ip1 = '1.1.1.1';
    const ip2 = '2.2.2.2';

    // Khóa email1 tại ip1
    for (let i = 1; i <= 5; i++) {
      loginThrottle.recordFailure(email1, ip1, Date.now());
    }
    expect(() => loginThrottle.assertNotLocked(email1, ip1, Date.now())).toThrow();

    // email1 tại ip2 khác: không bị khóa
    expect(() => loginThrottle.assertNotLocked(email1, ip2, Date.now())).not.toThrow();

    // email2 tại ip1 khác: không bị khóa
    expect(() => loginThrottle.assertNotLocked(email2, ip1, Date.now())).not.toThrow();
  });
});
