import type { ApiError } from '../../../../shared/api';
import type { CheckoutPreviewInput, PaymentMethod } from './checkout-api';
import {
  ADDRESS_LINE_MAX_LENGTH,
  type CheckoutFieldErrors,
  type CheckoutFieldName,
} from './checkout-form-schema';

// Câu cố định của tiêu chí nghiệm thu khi shipping không báo được phí.
export const SHIPPING_UNAVAILABLE_MESSAGE = 'Chưa tính được phí vận chuyển, thử lại';

// Thứ tự ô hiện trên form. Phải khớp thứ tự ô trong `checkout-page.tsx`, vì ô lỗi đầu tiên theo thứ tự này nhận focus.
const FIELD_ORDER: CheckoutFieldName[] = [
  'contact.name',
  'contact.phone',
  'contact.email',
  'address',
  'address.provinceCode',
  'address.wardCode',
  'address.line',
  'note',
  'paymentMethod',
  'invoice',
  'invoice.companyName',
  'invoice.taxCode',
  'invoice.email',
  'invoice.address',
];

interface AddressForPreview {
  provinceCode: string;
  wardCode: string;
  line: string;
}

// Chỉ trả dữ liệu gọi preview khi địa chỉ đủ tỉnh, phường và số nhà. Địa chỉ thiếu thì không tốn lượt giới hạn.
export function buildPreviewInput(
  address: AddressForPreview,
  paymentMethod: PaymentMethod,
): CheckoutPreviewInput | null {
  // Cắt khoảng trắng trước khi gửi, vì backend cũng cắt và so độ dài trên chuỗi đã cắt.
  const line = address.line.trim();
  const isComplete =
    address.provinceCode !== '' &&
    address.wardCode !== '' &&
    line !== '' &&
    line.length <= ADDRESS_LINE_MAX_LENGTH;
  if (!isComplete) return null;

  return {
    address: { provinceCode: address.provinceCode, wardCode: address.wardCode, line },
    paymentMethod,
  };
}

export function isCheckoutFieldName(key: string): key is CheckoutFieldName {
  return (FIELD_ORDER as string[]).includes(key);
}

export interface MappedServerErrors {
  fieldErrors: CheckoutFieldErrors;
  // Lỗi có `key` không thuộc ô nào. Giao diện hiện các câu này trong thông báo chung.
  unmatched: string[];
}

// Gán từng phần tử `details` của API vào ô cùng tên.
export function mapServerDetailsToFieldErrors(
  details: { key: string; message: string }[] | undefined,
): MappedServerErrors {
  const fieldErrors: CheckoutFieldErrors = {};
  const unmatched: string[] = [];

  for (const detail of details ?? []) {
    if (!isCheckoutFieldName(detail.key)) {
      unmatched.push(detail.message);
      continue;
    }
    // Giữ lỗi đầu tiên của ô, giống cách kiểm phía client.
    if (!fieldErrors[detail.key]) fieldErrors[detail.key] = detail.message;
  }

  return { fieldErrors, unmatched };
}

export function firstErrorField(errors: CheckoutFieldErrors): CheckoutFieldName | null {
  return FIELD_ORDER.find((field) => Boolean(errors[field])) ?? null;
}

export type PreviewFailure =
  | { kind: 'shipping-unavailable'; message: string }
  | { kind: 'cod-not-allowed'; message: string }
  | { kind: 'address'; fieldErrors: CheckoutFieldErrors }
  | { kind: 'cart-problem'; message: string }
  | { kind: 'rate-limited'; message: string }
  | { kind: 'other'; message: string };

const SHIPPING_ERROR_CODES = ['SHIPPING_UNAVAILABLE', 'SHIPPING_NOT_CONFIGURED'];

// Phân loại lỗi preview để trang chọn đúng cách hiện: dưới ô, thông báo kèm liên kết giỏ, hoặc nút thử lại.
export function describePreviewError(error: ApiError): PreviewFailure {
  if (SHIPPING_ERROR_CODES.includes(error.code)) {
    // Dùng một câu cố định cho cả hai mã. Khách không cần biết shop thiếu cấu hình hay GHN gián đoạn.
    return { kind: 'shipping-unavailable', message: SHIPPING_UNAVAILABLE_MESSAGE };
  }
  if (error.code === 'COD_NOT_ALLOWED') {
    return { kind: 'cod-not-allowed', message: error.message };
  }
  if (error.code === 'TOO_MANY_REQUESTS') {
    return { kind: 'rate-limited', message: error.message };
  }
  if (error.code === 'SKU_UNAVAILABLE') {
    return { kind: 'cart-problem', message: error.message };
  }

  const cartDetail = error.details?.find((detail) => detail.key === 'cart');
  if (cartDetail) {
    return { kind: 'cart-problem', message: cartDetail.message };
  }

  const { fieldErrors } = mapServerDetailsToFieldErrors(error.details);
  const addressErrors: CheckoutFieldErrors = {};
  for (const field of FIELD_ORDER) {
    if (field.startsWith('address') && fieldErrors[field]) {
      addressErrors[field] = fieldErrors[field];
    }
  }
  if (Object.keys(addressErrors).length > 0) {
    return { kind: 'address', fieldErrors: addressErrors };
  }

  return { kind: 'other', message: error.message };
}

export interface StockShortage {
  skuId: string;
  message: string;
}

export type OrderFailure =
  | { kind: 'total-changed'; message: string }
  | { kind: 'insufficient-stock'; message: string; shortages: StockShortage[] }
  | { kind: 'field-errors'; fieldErrors: CheckoutFieldErrors; unmatched: string[] }
  | { kind: 'other'; message: string };

// Phân loại lỗi đặt đơn. Mỗi loại có cách hiện khác nhau và không loại nào tự đặt lại đơn.
export function describeOrderError(error: ApiError): OrderFailure {
  if (error.code === 'ORDER_TOTAL_CHANGED') {
    return { kind: 'total-changed', message: error.message };
  }

  if (error.code === 'INSUFFICIENT_STOCK') {
    // Với lỗi này `key` của mỗi phần tử là mã SKU thiếu hàng, không phải tên ô.
    const shortages = (error.details ?? []).map((detail) => ({
      skuId: detail.key,
      message: detail.message,
    }));
    return { kind: 'insufficient-stock', message: error.message, shortages };
  }

  if (error.code === 'VALIDATION_ERROR' && error.details && error.details.length > 0) {
    const { fieldErrors, unmatched } = mapServerDetailsToFieldErrors(error.details);
    return { kind: 'field-errors', fieldErrors, unmatched };
  }

  return { kind: 'other', message: error.message };
}
