import { describe, expect, it } from 'vitest';
import {
  formatOrderFlag,
  formatOrderStatus,
  formatPaymentMethod,
  formatPaymentStatus,
  formatShipmentStatus,
  ORDER_FLAG_CODES,
  ORDER_STATUS_CODES,
} from '../../../../src/features/commerce/order-labels';

describe('formatOrderStatus', () => {
  it('chuyển đổi đúng toàn bộ 7 trạng thái đơn hàng sang tiếng Việt', () => {
    // Kiểm tra từng trạng thái đơn hàng theo máy trạng thái.
    expect(formatOrderStatus('pending_payment')).toBe('Chờ thanh toán');
    expect(formatOrderStatus('pending_confirmation')).toBe('Chờ xác nhận');
    expect(formatOrderStatus('confirmed')).toBe('Đã xác nhận');
    expect(formatOrderStatus('shipping')).toBe('Đang giao hàng');
    expect(formatOrderStatus('delivered')).toBe('Đã giao hàng');
    expect(formatOrderStatus('returned')).toBe('Đã trả hàng');
    expect(formatOrderStatus('cancelled')).toBe('Đã hủy');
  });

  it('giữ nguyên chuỗi khi gặp giá trị trạng thái lạ', () => {
    // Giá trị lạ không được làm lỗi giao diện mà giữ nguyên chuỗi.
    expect(formatOrderStatus('unknown_status')).toBe('unknown_status');
    expect(formatOrderStatus('')).toBe('');
  });
});

describe('formatPaymentStatus', () => {
  it('chuyển đổi đúng toàn bộ 4 trạng thái thanh toán sang tiếng Việt', () => {
    // Kiểm tra từng trạng thái thanh toán theo tài liệu đặc tả.
    expect(formatPaymentStatus('unpaid')).toBe('Chưa thanh toán');
    expect(formatPaymentStatus('paid')).toBe('Đã thanh toán');
    expect(formatPaymentStatus('refund_pending')).toBe('Chờ hoàn tiền');
    expect(formatPaymentStatus('refunded')).toBe('Đã hoàn tiền');
  });

  it('giữ nguyên chuỗi khi gặp giá trị thanh toán lạ', () => {
    // Không làm lỗi trang khi gặp trạng thái mới chưa hỗ trợ.
    expect(formatPaymentStatus('custom_status')).toBe('custom_status');
  });
});

describe('formatPaymentMethod', () => {
  it('chuyển đổi đúng các phương thức thanh toán sang tiếng Việt', () => {
    // Kiểm tra phương thức COD và chuyển khoản SePay.
    expect(formatPaymentMethod('cod')).toBe('Thanh toán khi nhận hàng (COD)');
    expect(formatPaymentMethod('sepay')).toBe('Chuyển khoản ngân hàng');
  });

  it('giữ nguyên chuỗi khi gặp phương thức thanh toán lạ', () => {
    // Phương thức thanh toán chưa biết được giữ nguyên tên gốc.
    expect(formatPaymentMethod('momo')).toBe('momo');
  });
});

describe('formatOrderFlag', () => {
  it('chuyển đổi đúng sáu cờ đơn sang tiếng Việt', () => {
    expect(formatOrderFlag('underpaid')).toBe('Thanh toán thiếu');
    expect(formatOrderFlag('overpaid')).toBe('Thanh toán dư');
    expect(formatOrderFlag('late_payment')).toBe('Thanh toán trễ');
    expect(formatOrderFlag('payment_expired')).toBe('Quá hạn thanh toán');
    expect(formatOrderFlag('invoice_issued')).toBe('Đã xuất hóa đơn');
    expect(formatOrderFlag('shipment_cancelled')).toBe('Vận đơn đã hủy');
  });

  it('giữ nguyên chuỗi khi gặp cờ lạ', () => {
    expect(formatOrderFlag('future_flag')).toBe('future_flag');
  });

  it('danh sách mã cờ gồm đúng sáu cờ theo thứ tự của backend', () => {
    expect(ORDER_FLAG_CODES).toEqual([
      'underpaid',
      'overpaid',
      'late_payment',
      'payment_expired',
      'invoice_issued',
      'shipment_cancelled',
    ]);
  });
});

describe('formatShipmentStatus', () => {
  it('chuyển đổi đúng chín trạng thái vận đơn sang tiếng Việt', () => {
    expect(formatShipmentStatus('pending_pickup')).toBe('Chờ lấy hàng');
    expect(formatShipmentStatus('in_transit')).toBe('Đang vận chuyển');
    expect(formatShipmentStatus('delivering')).toBe('Đang giao');
    expect(formatShipmentStatus('delivered')).toBe('Đã giao');
    expect(formatShipmentStatus('delivery_failed')).toBe('Giao thất bại');
    expect(formatShipmentStatus('returning')).toBe('Đang hoàn hàng');
    expect(formatShipmentStatus('returned')).toBe('Đã hoàn hàng');
    expect(formatShipmentStatus('cancelled')).toBe('Đã hủy');
    expect(formatShipmentStatus('exception')).toBe('Có sự cố');
  });

  it('giữ nguyên chuỗi khi gặp trạng thái lạ', () => {
    expect(formatShipmentStatus('lost')).toBe('lost');
  });
});

describe('ORDER_STATUS_CODES', () => {
  it('liệt kê bảy trạng thái đơn theo thứ tự của vòng đời đơn', () => {
    expect(ORDER_STATUS_CODES).toEqual([
      'pending_payment',
      'pending_confirmation',
      'confirmed',
      'shipping',
      'delivered',
      'returned',
      'cancelled',
    ]);
  });
});
