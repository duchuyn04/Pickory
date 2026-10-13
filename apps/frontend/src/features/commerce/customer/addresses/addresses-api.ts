import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '../../../../shared/api';
import type { AddressValue } from '../../../shipping/address-picker';

// Khóa truy vấn của sổ địa chỉ. Sau thêm, sửa, xóa, đặt mặc định thì đánh dấu cũ khóa này.
export const MY_ADDRESSES_KEY = ['my-addresses'] as const;

// Mỗi tài khoản lưu tối đa 10 địa chỉ. Khớp `SavedAddressService` của backend.
export const SAVED_ADDRESS_LIMIT = 10;

// Một địa chỉ trong sổ. `address` cùng dạng với địa chỉ giao hàng của đơn.
export interface SavedAddress {
  id: string;
  label: string;
  name: string;
  phone: string;
  address: AddressValue;
  isDefault: boolean;
}

// Body tạo địa chỉ. Địa chỉ chỉ gửi mã tỉnh, mã phường và số nhà, backend tra lại tên từ danh mục.
export interface SavedAddressBody {
  label: string;
  name: string;
  phone: string;
  address: { provinceCode: string; wardCode: string; line: string };
  isDefault: boolean;
}

// Body sửa một phần. Trường `address`, nếu có, phải gửi đủ ba trường.
export type SavedAddressChanges = Partial<SavedAddressBody>;

export function fetchSavedAddresses(): Promise<SavedAddress[]> {
  return apiRequest<SavedAddress[]>('/api/me/addresses');
}

export function createSavedAddress(body: SavedAddressBody): Promise<SavedAddress> {
  return apiRequest<SavedAddress>('/api/me/addresses', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function updateSavedAddress(
  id: string,
  changes: SavedAddressChanges,
): Promise<SavedAddress> {
  return apiRequest<SavedAddress>(`/api/me/addresses/${encodeURIComponent(id)}`, {
    method: 'PATCH',
    body: JSON.stringify(changes),
  });
}

export function deleteSavedAddress(id: string): Promise<void> {
  return apiRequest<void>(`/api/me/addresses/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

// Sổ địa chỉ của khách. Chỉ khách đã đăng nhập mới có sổ, nên khách vãng lai và nhân viên truyền `false`.
export function useSavedAddresses(isEnabled: boolean) {
  return useQuery<SavedAddress[]>({
    queryKey: MY_ADDRESSES_KEY,
    queryFn: fetchSavedAddresses,
    enabled: isEnabled,
  });
}

// Địa chỉ một dòng để hiện trong danh sách và ô chọn: số nhà, phường, tỉnh.
export function describeAddress(address: AddressValue): string {
  return [address.line, address.wardName, address.provinceName]
    .filter((part) => part !== '')
    .join(', ');
}
