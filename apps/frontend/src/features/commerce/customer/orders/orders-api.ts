import { apiRequest } from '../../../../shared/api';

// Một dòng sản phẩm trong chi tiết đơn hàng.
export interface OrderDetailItem {
  name: string;
  variantName: string | null;
  imagePublicId: string | null;
  imageUrl: string | null;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

// Một mốc thay đổi trạng thái trong tiến trình đơn hàng.
export interface OrderStatusHistoryEntry {
  fromStatus: string;
  toStatus: string;
  createdAt: string;
}

// Thông tin người nhận hàng trong bản chụp của đơn.
export interface OrderContact {
  name: string;
  phone: string;
  email: string;
}

// Địa chỉ giao hàng đã lưu trong bản chụp của đơn.
export interface OrderShippingAddress {
  provinceCode: string;
  provinceName?: string;
  wardCode: string;
  wardName?: string;
  line: string;
}

// Thông tin xuất hóa đơn VAT nếu khách yêu cầu lúc đặt hàng.
export interface OrderInvoice {
  companyName: string;
  taxCode: string;
  address: string;
  email: string;
}

// Dữ liệu chi tiết của đơn hàng phục vụ trang theo dõi đơn.
export interface OrderDetail {
  code: string;
  status: string;
  paymentStatus: string;
  paymentMethod: string;
  createdAt: string;
  paymentDeadline: string | null;
  items: OrderDetailItem[];
  subtotal: number;
  shippingFee: number;
  shippingDiscount: number;
  total: number;
  contact: OrderContact;
  shippingAddress: OrderShippingAddress;
  invoice: OrderInvoice | null;
  statusHistory: OrderStatusHistoryEntry[];
  canCancel: boolean;
  trackingCode: string | null;
}

// Một dòng tóm tắt đơn trong danh sách đơn của khách hàng.
export interface OrderListItem {
  code: string;
  status: string;
  paymentStatus: string;
  paymentMethod: string;
  total: number;
  createdAt: string;
  itemCount: number;
  firstItemName: string;
  firstItemImagePublicId: string | null;
  firstItemImageUrl: string | null;
}

// Kết quả phân trang danh sách đơn hàng của khách.
export interface CustomerOrdersPagedResponse {
  items: OrderListItem[];
  total: number;
  page: number;
  limit: number;
}

// Gọi API lấy dữ liệu chi tiết của một đơn hàng theo mã đơn và token.
export function fetchOrderDetail(code: string, token?: string | null): Promise<OrderDetail> {
  const query = token ? `?t=${encodeURIComponent(token)}` : '';
  return apiRequest<OrderDetail>(`/api/orders/${encodeURIComponent(code)}${query}`);
}

// Gọi API tự hủy đơn hàng bằng phương thức POST kèm token khi có.
export function cancelOrder(code: string, token?: string | null): Promise<OrderDetail> {
  const query = token ? `?t=${encodeURIComponent(token)}` : '';
  return apiRequest<OrderDetail>(`/api/orders/${encodeURIComponent(code)}/cancellation${query}`, {
    method: 'POST',
  });
}

// Gọi API lấy danh sách lịch sử đơn hàng phân trang của tài khoản.
export function fetchCustomerOrders(page = 1, limit = 20): Promise<CustomerOrdersPagedResponse> {
  return apiRequest<CustomerOrdersPagedResponse>(`/api/orders?page=${page}&limit=${limit}`);
}
