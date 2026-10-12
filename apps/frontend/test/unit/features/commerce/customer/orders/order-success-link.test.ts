import { describe, expect, it } from 'vitest';
import { buildOrderSuccessLink } from '../../../../../../src/features/commerce/customer/orders/order-success-link';

describe('buildOrderSuccessLink', () => {
  it('trả về liên kết kèm token khi có accessToken trong state', () => {
    // Có token thì khách vãng lai hay tài khoản đều nhận link kèm token.
    const result = buildOrderSuccessLink('PK12345678', 'token-xyz', false);
    expect(result).toEqual({ href: '/orders/PK12345678?t=token-xyz' });
  });

  it('trả về liên kết không kèm token khi khách đã đăng nhập và không có token', () => {
    // Khách đã đăng nhập mở lại trang hoặc replay khóa thì xem đơn qua tài khoản.
    const result = buildOrderSuccessLink('PK12345678', null, true);
    expect(result).toEqual({ href: '/orders/PK12345678' });
  });

  it('không trả liên kết và thông báo gửi email khi khách vãng lai không có token', () => {
    // Khách vãng lai không có token thì không xem được đơn nên hướng dẫn kiểm tra email.
    const result = buildOrderSuccessLink('PK12345678', null, false);
    expect(result).toEqual({ message: 'Liên kết xem đơn đã gửi tới email của bạn.' });
  });

  it('mã hóa mã đơn và token trong URL để tránh lỗi ký tự đặc biệt', () => {
    // Escape các ký tự đặc biệt trong mã đơn và token.
    const result = buildOrderSuccessLink('PK/123', 'tok&val', false);
    expect(result).toEqual({ href: '/orders/PK%2F123?t=tok%26val' });
  });
});
