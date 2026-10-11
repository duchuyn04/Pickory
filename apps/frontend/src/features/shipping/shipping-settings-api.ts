import { apiRequest } from '../../shared/api';

// Cấu hình lấy hàng đã lưu. Khớp `ShippingSettingsView` của backend.
export interface ShippingSettings {
  senderName: string;
  senderPhone: string;
  provinceCode: string;
  provinceName: string;
  wardCode: string;
  wardName: string;
  line: string;
  // `null` nghĩa là chưa chọn. Backend khi đó báo phí theo loại hàng nhẹ (2).
  defaultServiceTypeId: number | null;
  updatedAt: string;
}

// Thân của GET và PUT. `ghnShopId` chỉ để xem, đến từ biến môi trường của backend.
export interface ShippingSettingsResponse {
  settings: ShippingSettings | null;
  ghnShopId: string | null;
}

// Sáu trường PUT nhận. Backend từ chối mọi trường khác, kể cả tên tỉnh và tên phường.
export interface ShippingSettingsPayload {
  senderName: string;
  senderPhone: string;
  provinceCode: string;
  wardCode: string;
  line: string;
  defaultServiceTypeId: number | null;
}

const SETTINGS_PATH = '/api/admin/shipping/settings';

export const shippingSettingsQueryKey = ['admin-shipping-settings'] as const;

export function fetchShippingSettings(): Promise<ShippingSettingsResponse> {
  return apiRequest<ShippingSettingsResponse>(SETTINGS_PATH);
}

export function saveShippingSettings(
  payload: ShippingSettingsPayload,
): Promise<ShippingSettingsResponse> {
  return apiRequest<ShippingSettingsResponse>(SETTINGS_PATH, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}
