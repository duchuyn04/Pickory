// Nhãn dùng chung cho màn của khách và màn của nhân viên, để một trạng thái chỉ có một tên.

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

// Thứ tự trạng thái theo vòng đời đơn. Tab của nhân viên hiện theo thứ tự này.
export const ORDER_STATUS_CODES = [
  'pending_payment',
  'pending_confirmation',
  'confirmed',
  'shipping',
  'delivered',
  'returned',
  'cancelled',
] as const;

export const PAYMENT_STATUS_CODES = ['unpaid', 'paid', 'refund_pending', 'refunded'] as const;

export const PAYMENT_METHOD_CODES = ['cod', 'sepay'] as const;

// Cờ đơn khớp `AdminOrderListQuery.KnownFlags` của backend, theo cùng thứ tự.
export const ORDER_FLAG_CODES = [
  'underpaid',
  'overpaid',
  'late_payment',
  'payment_expired',
  'invoice_issued',
  'shipment_cancelled',
] as const;

// Bảng ánh xạ cờ đơn sang tiếng Việt. Cờ ghi lại điều bất thường nhân viên cần chú ý.
const ORDER_FLAG_LABELS: Record<string, string> = {
  underpaid: 'Thanh toán thiếu',
  overpaid: 'Thanh toán dư',
  late_payment: 'Thanh toán trễ',
  payment_expired: 'Quá hạn thanh toán',
  invoice_issued: 'Đã xuất hóa đơn',
  shipment_cancelled: 'Vận đơn đã hủy',
};

// Bảng ánh xạ trạng thái vận đơn của module Shipping sang tiếng Việt.
const SHIPMENT_STATUS_LABELS: Record<string, string> = {
  pending_pickup: 'Chờ lấy hàng',
  in_transit: 'Đang vận chuyển',
  delivering: 'Đang giao',
  delivered: 'Đã giao',
  delivery_failed: 'Giao thất bại',
  returning: 'Đang hoàn hàng',
  returned: 'Đã hoàn hàng',
  cancelled: 'Đã hủy',
  exception: 'Có sự cố',
};

// Chuyển mã cờ đơn sang nhãn tiếng Việt.
export function formatOrderFlag(flag: string): string {
  // Cờ mới của backend chưa có trong bảng vẫn hiện nguyên mã, để nhân viên không mất thông tin.
  return ORDER_FLAG_LABELS[flag] ?? flag;
}

// Chuyển trạng thái vận đơn sang nhãn tiếng Việt.
export function formatShipmentStatus(status: string): string {
  // Trạng thái lạ giữ nguyên mã để màn không lỗi khi module Shipping thêm trạng thái mới.
  return SHIPMENT_STATUS_LABELS[status] ?? status;
}
