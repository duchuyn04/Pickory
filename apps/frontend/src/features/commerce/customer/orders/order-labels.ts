// Bảng ánh xạ trạng thái đơn hàng sang tiếng Việt hiển thị cho khách.
const ORDER_STATUS_LABELS: Record<string, string> = {
  pending_payment: 'Chờ thanh toán',
  pending_confirmation: 'Chờ xác nhận',
  confirmed: 'Đã xác nhận',
  shipping: 'Đang giao hàng',
  delivered: 'Đã giao hàng',
  returned: 'Đã trả hàng',
  cancelled: 'Đã hủy',
};

// Bảng ánh xạ trạng thái thanh toán sang tiếng Việt hiển thị cho khách.
const PAYMENT_STATUS_LABELS: Record<string, string> = {
  unpaid: 'Chưa thanh toán',
  paid: 'Đã thanh toán',
  refund_pending: 'Chờ hoàn tiền',
  refunded: 'Đã hoàn tiền',
};

// Bảng ánh xạ phương thức thanh toán sang tiếng Việt.
const PAYMENT_METHOD_LABELS: Record<string, string> = {
  cod: 'Thanh toán khi nhận hàng (COD)',
  sepay: 'Chuyển khoản ngân hàng',
};

// Chuyển đổi mã trạng thái đơn hàng sang nhãn tiếng Việt dễ hiểu.
export function formatOrderStatus(status: string): string {
  // Trả nguyên chuỗi khi gặp giá trị lạ để không làm lỗi màn hình.
  return ORDER_STATUS_LABELS[status] ?? status;
}

// Chuyển đổi trạng thái thanh toán sang nhãn tiếng Việt dễ hiểu.
export function formatPaymentStatus(status: string): string {
  // Trả nguyên chuỗi khi gặp trạng thái chưa có trong bảng ánh xạ.
  return PAYMENT_STATUS_LABELS[status] ?? status;
}

// Chuyển đổi phương thức thanh toán sang nhãn tiếng Việt hiển thị.
export function formatPaymentMethod(method: string): string {
  // Trả nguyên mã phương thức khi gặp giá trị mới chưa hỗ trợ.
  return PAYMENT_METHOD_LABELS[method] ?? method;
}
