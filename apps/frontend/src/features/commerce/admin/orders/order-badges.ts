// Lớp badge theo trạng thái. Màu theo mức cần chú ý: xanh là xong, vàng cần theo dõi, đỏ là hỏng hoặc hủy.

const ORDER_STATUS_BADGES: Record<string, string> = {
  pending_payment: 'badge badge-warn',
  pending_confirmation: 'badge badge-info',
  confirmed: 'badge badge-info',
  shipping: 'badge badge-info',
  delivered: 'badge badge-ok',
  returned: 'badge badge-warn',
  cancelled: 'badge badge-bad',
};

const PAYMENT_STATUS_BADGES: Record<string, string> = {
  unpaid: 'badge badge-warn',
  paid: 'badge badge-ok',
  refund_pending: 'badge badge-warn',
  refunded: 'badge badge-info',
};

const ORDER_FLAG_BADGES: Record<string, string> = {
  underpaid: 'badge badge-warn',
  overpaid: 'badge badge-warn',
  late_payment: 'badge badge-warn',
  payment_expired: 'badge badge-bad',
  invoice_issued: 'badge badge-info',
  shipment_cancelled: 'badge badge-bad',
};

// Phí đã lưu và phí dự phòng dùng nhãn vàng để nhân viên chú ý. Trang chỉ gọi hàm này khi đơn không dùng phí GHN vừa tính.
export function substituteFeeBadgeClass(): string {
  return 'badge badge-warn';
}

// Mã lạ dùng badge trung tính, để màn vẫn hiện được khi backend thêm trạng thái mới.
export function orderStatusBadgeClass(status: string): string {
  return ORDER_STATUS_BADGES[status] ?? 'badge';
}

export function paymentStatusBadgeClass(status: string): string {
  return PAYMENT_STATUS_BADGES[status] ?? 'badge';
}

export function orderFlagBadgeClass(flag: string): string {
  return ORDER_FLAG_BADGES[flag] ?? 'badge';
}
