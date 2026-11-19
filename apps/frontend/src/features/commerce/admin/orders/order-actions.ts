import type { AdminOrderDetail } from './admin-orders-api';

// Phần của chi tiết đơn mà việc chọn nút cần. Nhận mọi kiểu có đủ các trường này.
export type OrderActionSubject = Pick<
  AdminOrderDetail,
  'status' | 'paymentStatus' | 'paymentMethod' | 'flags'
> & {
  invoice: unknown | null;
  shipment: unknown | null;
};

export type OrderActionKey =
  | 'confirm'
  | 'createShipment'
  | 'cancelShipment'
  | 'startManualShipping'
  | 'returnToConfirmed'
  | 'issueInvoice'
  | 'receiveCod'
  | 'refund'
  | 'cancel';

// Quyền của từng route. Khớp `AdminOrderEndpoints` của backend.
export const ORDER_ACTION_PERMISSIONS: Record<OrderActionKey, string> = {
  confirm: 'orders:update-status',
  createShipment: 'orders:fulfill',
  cancelShipment: 'orders:fulfill',
  // Hai thao tác giao thủ công cùng đi qua route `fulfillment`, nên cùng quyền với vận đơn.
  startManualShipping: 'orders:fulfill',
  returnToConfirmed: 'orders:fulfill',
  issueInvoice: 'orders:update-status',
  receiveCod: 'payments:reconcile',
  refund: 'refunds:manage',
  cancel: 'orders:cancel',
};

// Trạng thái mà backend cho phép hủy. Đơn đang giao phải hủy vận đơn trước.
const CANCELLABLE_STATUSES = ['pending_payment', 'pending_confirmation', 'confirmed'];

// Cờ ghi nhận hóa đơn đã xuất. Khớp hằng của backend.
const INVOICE_ISSUED_FLAG = 'invoice_issued';

// Cờ backend gắn vào đơn khi shop tự giao. Khớp hằng của backend.
const MANUAL_SHIPPING_FLAG = 'manual_shipping';

// Điều kiện về trạng thái đơn của từng thao tác, không tính quyền.
function isActionApplicable(action: OrderActionKey, order: OrderActionSubject): boolean {
  switch (action) {
    case 'confirm':
      return order.status === 'pending_confirmation';
    case 'createShipment':
      return order.status === 'confirmed';
    case 'cancelShipment':
      return order.status === 'shipping' && order.shipment !== null;
    case 'startManualShipping':
      return order.status === 'confirmed';
    case 'returnToConfirmed':
      // Chỉ đơn do shop tự giao mới được quay về. Đơn giao qua GHN phải hủy vận đơn.
      return order.status === 'shipping' && order.flags.includes(MANUAL_SHIPPING_FLAG);
    case 'issueInvoice':
      // Hóa đơn chỉ xuất cho đơn còn hiệu lực và có yêu cầu hóa đơn, và chỉ xuất một lần.
      return (
        order.invoice !== null &&
        !order.flags.includes(INVOICE_ISSUED_FLAG) &&
        order.status !== 'cancelled' &&
        order.status !== 'returned'
      );
    case 'receiveCod':
      return (
        order.paymentMethod === 'cod' &&
        order.status === 'delivered' &&
        order.paymentStatus === 'unpaid'
      );
    case 'refund':
      return order.paymentStatus === 'refund_pending';
    case 'cancel':
      return CANCELLABLE_STATUSES.includes(order.status);
  }
}

// Thứ tự nút trên màn: nút nguy hiểm đứng đầu, nút phụ ở giữa, nút chính ngoài cùng bên phải.
// Nút phụ và nút nguy hiểm dùng kiểu khác nút chính.
const ACTION_ORDER: OrderActionKey[] = [
  'cancelShipment',
  'cancel',
  'returnToConfirmed',
  'startManualShipping',
  'issueInvoice',
  'confirm',
  'createShipment',
  'receiveCod',
  'refund',
];

// Nút nhân viên thấy trên một đơn cần đủ hai điều kiện: quyền của route và trạng thái route chấp nhận.
// Backend vẫn kiểm cả hai ở mọi route.
export function availableOrderActions(
  order: OrderActionSubject,
  can: (permission: string) => boolean,
): OrderActionKey[] {
  return ACTION_ORDER.filter(
    (action) => can(ORDER_ACTION_PERMISSIONS[action]) && isActionApplicable(action, order),
  );
}
