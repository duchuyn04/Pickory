import { describe, expect, it } from 'vitest';
import {
  formatOrderStatus,
  formatPaymentMethod,
  formatPaymentStatus,
} from '../../../../../../src/features/commerce/customer/orders/order-labels';

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
