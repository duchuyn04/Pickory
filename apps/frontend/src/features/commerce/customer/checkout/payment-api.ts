import { apiRequest } from '../../../../shared/api';

export interface OrderPayment {
  code: string;
  status: string;
  paymentStatus: string;
  paymentMethod: string;
  total: number;
  paymentDeadline: string | null;
  bankAccountNumber: string | null;
  bankCode: string | null;
}

export function fetchOrderPayment(code: string, token: string | null): Promise<OrderPayment> {
  // Token của khách vãng lai phải theo request để tải lại trang vẫn xem được đơn.
  const query = token ? `?t=${encodeURIComponent(token)}` : '';
  return apiRequest<OrderPayment>(`/api/orders/${encodeURIComponent(code)}/payment${query}`);
}
