import { apiRequest } from '../../shared/api';

// Một dòng của `GET /api/addresses/provinces` và `GET /api/addresses/wards`. Backend chỉ trả hai trường này.
export interface AddressOption {
  code: string;
  name: string;
}

// Khóa React Query: danh sách tỉnh/thành một khóa, phường/xã một khóa cho từng tỉnh/thành.
export const addressQueryKeys = {
  provinces: ['addresses', 'provinces'] as const,
  wards: (provinceCode: string) => ['addresses', 'wards', provinceCode] as const,
};

export function fetchProvinces(): Promise<AddressOption[]> {
  return apiRequest<AddressOption[]>('/api/addresses/provinces');
}

export function fetchWards(provinceCode: string): Promise<AddressOption[]> {
  // Mã tỉnh/thành đi qua query string, nên phải mã hóa để ký tự lạ không đổi nghĩa của URL.
  return apiRequest<AddressOption[]>(
    `/api/addresses/wards?provinceCode=${encodeURIComponent(provinceCode)}`,
  );
}
