import { apiRequest } from '../../../../shared/api';

// Chuỗi khớp `CheckoutPaymentMethods` của backend: `cod` là thanh toán khi nhận hàng, `sepay` là chuyển khoản VietQR.
export type PaymentMethod = 'cod' | 'sepay';

// Địa chỉ gửi lên. Backend từ chối trường lạ, nên không gửi tên tỉnh và tên phường.
export interface CheckoutAddressPayload {
  provinceCode: string;
  wardCode: string;
  line: string;
}

// Body của `POST /api/checkout/previews`. Body không có dòng hàng, vì server đọc giỏ của người gọi.
export interface CheckoutPreviewInput {
  address: CheckoutAddressPayload;
  paymentMethod: PaymentMethod;
}

// Kết quả preview, với mọi số tiền là đồng Việt Nam. Khớp `CheckoutPreviewResponse` của backend.
export interface CheckoutPreview {
  subtotal: number;
  shippingFee: number;
  shippingDiscount: number;
  customerShippingFee: number;
  total: number;
  freeShippingThreshold: number;
  codMaxAmount: number;
  codAllowed: boolean;
  expectedDeliveryDate: string | null;
}

export interface PlaceOrderContact {
  name: string;
  phone: string;
  email: string;
}

export interface PlaceOrderInvoice {
  companyName: string;
  taxCode: string;
  address: string;
  email: string;
}

// Body của `POST /api/orders`. `expectedTotal` là tổng của preview mà khách đang xem.
export interface PlaceOrderPayload {
  contact: PlaceOrderContact;
  address: CheckoutAddressPayload;
  note: string | null;
  paymentMethod: PaymentMethod;
  invoice: PlaceOrderInvoice | null;
  expectedTotal: number;
}

// Kết quả đặt đơn. `accessToken` chỉ có ở lần tạo đơn, lần gửi lại cùng khóa trả `null`.
export interface PlacedOrder {
  code: string;
  status: string;
  paymentStatus: string;
  paymentMethod: PaymentMethod;
  subtotal: number;
  shippingFee: number;
  shippingDiscount: number;
  total: number;
  paymentDeadline: string | null;
  accessToken: string | null;
}

export function fetchCheckoutPreview(input: CheckoutPreviewInput): Promise<CheckoutPreview> {
  return apiRequest<CheckoutPreview>('/api/checkout/previews', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export function placeOrder(
  payload: PlaceOrderPayload,
  idempotencyKey: string,
): Promise<PlacedOrder> {
  return apiRequest<PlacedOrder>('/api/orders', {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify(payload),
  });
}
