import { apiRequest } from '../../../../shared/api';
import {
  buildAdminOrdersPath,
  fetchAdminOrderDetail,
  runOrderMutation,
  type AdminOrderList,
} from '../orders/admin-orders-api';

// Tên tham số trên URL của màn đối soát. `tab` chọn tab, hai tham số còn lại là bộ lọc của tab SePay.
export const PAYMENT_FILTER_NAMES = ['tab', 'matchStatus', 'orderFlag'] as const;

export type PaymentTab = 'sepay' | 'cod';

// Giá trị của ô chọn "Tất cả" ở bộ lọc trạng thái khớp. Giá trị này không gửi cho API.
export const ALL_MATCH_STATUSES = 'all';

// Mặc định là việc nhân viên cần làm ngay: giao dịch chưa khớp đơn nào.
export const DEFAULT_MATCH_STATUS = 'unmatched';

export interface PaymentOrderSummary {
  code: string;
  status: string;
  paymentStatus: string;
  total: number;
  flags: string[];
}

// Một giao dịch SePay. `order` là `null` khi chưa gắn đơn.
export interface PaymentTransaction {
  id: string;
  providerTxId: string;
  amount: number;
  content: string;
  matchStatus: string;
  receivedAt: string;
  order: PaymentOrderSummary | null;
}

export interface PaymentTransactionList {
  items: PaymentTransaction[];
  total: number;
  page: number;
  limit: number;
}

// Dựng đường dẫn danh sách giao dịch. Chỉ gửi tham số có giá trị, bỏ `page` khi là trang 1.
export function buildTransactionsPath(
  filters: { matchStatus: string; orderFlag: string },
  page: number,
): string {
  const params = new URLSearchParams();
  // "Tất cả" nghĩa là không lọc theo trạng thái khớp, nên không gửi tham số.
  if (filters.matchStatus !== ALL_MATCH_STATUSES) params.set('matchStatus', filters.matchStatus);
  if (filters.orderFlag !== '') params.set('orderFlag', filters.orderFlag);
  if (page > 1) params.set('page', String(page));
  const queryString = params.toString();
  return queryString
    ? `/api/admin/payments/transactions?${queryString}`
    : '/api/admin/payments/transactions';
}

export function fetchPaymentTransactions(
  filters: { matchStatus: string; orderFlag: string },
  page: number,
) {
  return apiRequest<PaymentTransactionList>(buildTransactionsPath(filters, page));
}

// Gắn giao dịch vào một đơn, hoặc bỏ qua giao dịch. Body chỉ có đúng một trong hai dạng.
export type TransactionUpdate = { orderCode: string } | { matchStatus: 'ignored' };

export function updatePaymentTransaction(id: string, update: TransactionUpdate) {
  return apiRequest<PaymentTransaction>(
    `/api/admin/payments/transactions/${encodeURIComponent(id)}`,
    { method: 'PATCH', body: JSON.stringify(update) },
  );
}

// Bộ lọc cố định của tab COD: đơn COD đã giao mà chưa nhận tiền.
const COD_PENDING_FILTERS = {
  status: 'delivered',
  paymentMethod: 'cod',
  paymentStatus: 'unpaid',
};

export function fetchCodPendingOrders(page: number) {
  return apiRequest<AdminOrderList>(buildAdminOrdersPath(COD_PENDING_FILTERS, page));
}

export function receiveCodPayment(code: string) {
  return runOrderMutation(code, { kind: 'receiveCod' });
}

// Tra đơn theo mã để nhân viên xem tóm tắt trước khi gắn giao dịch.
export const fetchOrderForAttachment = fetchAdminOrderDetail;
