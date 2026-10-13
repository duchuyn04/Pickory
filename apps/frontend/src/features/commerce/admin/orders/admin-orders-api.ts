import { apiRequest } from '../../../../shared/api';

// Tên tham số bộ lọc trên URL. Trùng tên tham số của `GET /api/admin/orders`.
export const ORDER_FILTER_NAMES = [
  'search',
  'status',
  'paymentStatus',
  'paymentMethod',
  'flag',
  'from',
  'to',
] as const;

export type OrderFilterName = (typeof ORDER_FILTER_NAMES)[number];

// Một dòng tóm tắt đơn trong danh sách của nhân viên.
export interface AdminOrderListItem {
  code: string;
  createdAt: string;
  status: string;
  paymentStatus: string;
  paymentMethod: string;
  total: number;
  flags: string[];
  recipientName: string | null;
  recipientPhone: string | null;
  // Số dòng hàng, không phải tổng số lượng.
  lineCount: number;
}

export interface AdminOrderList {
  items: AdminOrderListItem[];
  total: number;
  page: number;
  limit: number;
}

// Dòng hàng của đơn. Cùng dạng với chi tiết đơn của khách.
export interface AdminOrderItem {
  skuId: string;
  name: string;
  variantName: string | null;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

// Một lần đổi trạng thái. `actorId` là `null` khi hệ thống hoặc khách vãng lai chuyển.
export interface AdminOrderHistoryEntry {
  fromStatus: string;
  toStatus: string;
  actorId: string | null;
  reason: string | null;
  createdAt: string;
}

export interface AdminOrderShipment {
  id: string;
  trackingCode: string;
  status: string;
  fee: number;
}

// Bản chụp người nhận lúc đặt. Thiếu trường thì API không ghi trường đó.
export interface AdminOrderContact {
  name?: string;
  phone?: string;
  email?: string;
}

export interface AdminOrderShippingAddress {
  provinceName?: string;
  wardName?: string;
  line?: string;
}

export interface AdminOrderInvoice {
  companyName?: string;
  taxCode?: string;
  address?: string;
  email?: string;
}

// Kiện hàng gợi ý để form tạo vận đơn điền sẵn. Chỉ có khi đơn `confirmed`.
export interface AdminOrderSuggestedParcel {
  weightGrams: number;
  lengthCm: number;
  widthCm: number;
  heightCm: number;
}

export interface AdminOrderDetail {
  code: string;
  status: string;
  paymentStatus: string;
  paymentMethod: string;
  createdAt: string;
  paymentDeadline: string | null;
  flags: string[];
  contact: AdminOrderContact;
  shippingAddress: AdminOrderShippingAddress;
  invoice: AdminOrderInvoice | null;
  items: AdminOrderItem[];
  subtotal: number;
  shippingFee: number;
  shippingDiscount: number;
  total: number;
  statusHistory: AdminOrderHistoryEntry[];
  shipment: AdminOrderShipment | null;
  suggestedParcel: AdminOrderSuggestedParcel | null;
  refundTransactionRef: string | null;
}

// Dựng đường dẫn API từ bộ lọc trên URL. Chỉ gửi tham số có giá trị, và bỏ `page` khi là trang 1.
export function buildAdminOrdersPath(values: Record<string, string>, page: number): string {
  // `URLSearchParams` mã hóa ký tự đặc biệt trong từ khóa để URL không sai.
  const params = new URLSearchParams();
  for (const name of ORDER_FILTER_NAMES) {
    const value = values[name];
    if (value !== undefined && value !== '') params.set(name, value);
  }
  if (page > 1) params.set('page', String(page));
  const queryString = params.toString();
  return queryString ? `/api/admin/orders?${queryString}` : '/api/admin/orders';
}

export function fetchAdminOrders(values: Record<string, string>, page: number) {
  return apiRequest<AdminOrderList>(buildAdminOrdersPath(values, page));
}

export function fetchAdminOrderDetail(code: string) {
  return apiRequest<AdminOrderDetail>(`/api/admin/orders/${encodeURIComponent(code)}`);
}

// Thao tác ghi lên một đơn. Mỗi thao tác là một tài nguyên con của đơn, theo `AdminOrderEndpoints`.
export type OrderMutation =
  | { kind: 'confirm' }
  | { kind: 'cancel'; reason: string }
  | { kind: 'issueInvoice' }
  | { kind: 'createShipment'; parcel: AdminOrderSuggestedParcel }
  | { kind: 'cancelShipment' }
  | { kind: 'receiveCod' }
  | { kind: 'refund'; transactionRef: string };

// Gọi route của thao tác và trả chi tiết đơn mới. Lỗi 400, 409, 422 thành `ApiError` mang thông báo của API.
export function runOrderMutation(code: string, mutation: OrderMutation) {
  const base = `/api/admin/orders/${encodeURIComponent(code)}`;
  switch (mutation.kind) {
    case 'confirm':
      return apiRequest<AdminOrderDetail>(`${base}/confirmation`, { method: 'POST' });
    case 'cancel':
      return apiRequest<AdminOrderDetail>(`${base}/cancellation`, {
        method: 'POST',
        body: JSON.stringify({ reason: mutation.reason }),
      });
    case 'issueInvoice':
      return apiRequest<AdminOrderDetail>(`${base}/invoice-issuance`, { method: 'PUT' });
    case 'createShipment':
      return apiRequest<AdminOrderDetail>(`${base}/shipment`, {
        method: 'POST',
        body: JSON.stringify({ parcel: mutation.parcel }),
      });
    case 'cancelShipment':
      return apiRequest<AdminOrderDetail>(`${base}/shipment`, { method: 'DELETE' });
    case 'receiveCod':
      return apiRequest<AdminOrderDetail>(`${base}/cod-receipt`, { method: 'PUT' });
    case 'refund':
      return apiRequest<AdminOrderDetail>(`${base}/refund`, {
        method: 'PUT',
        body: JSON.stringify({ transactionRef: mutation.transactionRef }),
      });
  }
}
