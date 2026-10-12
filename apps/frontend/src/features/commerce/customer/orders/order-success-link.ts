export type OrderSuccessLinkResult =
  { href: string; message?: never } | { message: string; href?: never };

// Dựng liên kết xem đơn hoặc thông báo cho trang đặt hàng thành công.
export function buildOrderSuccessLink(
  code: string,
  accessToken: string | null | undefined,
  isAuthenticated: boolean,
): OrderSuccessLinkResult {
  // Có token xem đơn thì tạo liên kết kèm token cho mọi đối tượng khách.
  if (accessToken && accessToken.trim().length > 0) {
    return {
      href: `/orders/${encodeURIComponent(code)}?t=${encodeURIComponent(accessToken)}`,
    };
  }

  // Khách đã đăng nhập mở lại trang thì xem đơn qua tài khoản mà không cần token.
  if (isAuthenticated) {
    return {
      href: `/orders/${encodeURIComponent(code)}`,
    };
  }

  // Khách vãng lai không có token thì không xem được đơn nên hướng dẫn kiểm tra thư.
  return {
    message: 'Liên kết xem đơn đã gửi tới email của bạn.',
  };
}
