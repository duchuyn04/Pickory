import { apiRequest } from '../../shared/api';

// Cấu hình bán hàng đã lưu. Khớp `CommerceSettingsView` của backend.
export interface CommerceSettings {
  // Số tiền là đồng Việt Nam.
  freeShippingThreshold: number;
  codMaxAmount: number;
  maxQuantityPerSku: number;
  maxCartLines: number;
  // Hai trường SePay cùng có giá trị hoặc cùng là `null`.
  sepayAccountNumber: string | null;
  sepayBankCode: string | null;
  // `null` khi shop chưa lưu cấu hình lần nào và backend trả giá trị mặc định.
  updatedAt: string | null;
}

// Sáu trường PUT nhận. Backend từ chối mọi trường khác, kể cả khóa webhook SePay.
export interface CommerceSettingsPayload {
  freeShippingThreshold: number;
  codMaxAmount: number;
  maxQuantityPerSku: number;
  maxCartLines: number;
  sepayAccountNumber: string | null;
  sepayBankCode: string | null;
}

const SETTINGS_PATH = '/api/admin/commerce/settings';

export const commerceSettingsQueryKey = ['admin-commerce-settings'] as const;

export function fetchCommerceSettings(): Promise<CommerceSettings> {
  return apiRequest<CommerceSettings>(SETTINGS_PATH);
}

export function saveCommerceSettings(payload: CommerceSettingsPayload): Promise<CommerceSettings> {
  return apiRequest<CommerceSettings>(SETTINGS_PATH, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}
