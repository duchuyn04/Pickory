import { useQuery } from '@tanstack/react-query';
import { useId } from 'react';
import { ErrorAlert } from '../../shared/error-alert';
import { FieldError, fieldErrorProps, type FieldErrors } from '../../shared/field-error';
import { addressQueryKeys, fetchProvinces, fetchWards, type AddressOption } from './address-api';
import { AddressCombobox } from './address-combobox';
import './address-picker.css';

// Địa chỉ giao hàng. Giữ cả mã và tên vì đơn hàng lưu mã và tên tại thời điểm đặt (spec shipping).
export interface AddressValue {
  provinceCode: string;
  provinceName: string;
  wardCode: string;
  wardName: string;
  line: string;
}

export const EMPTY_ADDRESS: AddressValue = {
  provinceCode: '',
  provinceName: '',
  wardCode: '',
  wardName: '',
  line: '',
};

export type AddressFieldErrors = FieldErrors<'province' | 'ward' | 'line'>;

interface AddressPickerProps {
  value: AddressValue;
  onChange: (next: AddressValue) => void;
  // Form cha quyết định lỗi và truyền vào. Component không tự kiểm hợp lệ.
  errors?: AddressFieldErrors;
  // Đặt khi form cha cần id ổn định, ví dụ cho E2E. Bỏ trống thì component tự sinh id không trùng.
  idPrefix?: string;
  disabled?: boolean;
}

// Danh mục đồng bộ từ GHN mỗi 24 giờ, nên cache một giờ an toàn khi khách đổi qua lại giữa các tỉnh/thành.
// `gcTime` bằng `staleTime` để cache còn sau khi ô bị gỡ.
const ADDRESS_CACHE_MS = 60 * 60 * 1000;

// Ô chọn địa chỉ ba phần: tỉnh/thành, phường/xã của tỉnh/thành đó, rồi số nhà.
export function AddressPicker({
  value,
  onChange,
  errors = {},
  idPrefix,
  disabled = false,
}: AddressPickerProps) {
  const generatedId = useId().replace(/:/g, '');
  const baseId = idPrefix ?? `address-${generatedId}`;
  const provinceId = `${baseId}-province`;
  const wardId = `${baseId}-ward`;
  const lineId = `${baseId}-line`;

  const provincesQuery = useQuery({
    queryKey: addressQueryKeys.provinces,
    queryFn: fetchProvinces,
    staleTime: ADDRESS_CACHE_MS,
    gcTime: ADDRESS_CACHE_MS,
  });

  const hasProvince = value.provinceCode !== '';
  // Khóa theo mã tỉnh/thành, nên đổi tỉnh/thành không bao giờ hiện danh sách phường/xã của tỉnh trước.
  const wardsQuery = useQuery({
    queryKey: addressQueryKeys.wards(value.provinceCode),
    queryFn: () => fetchWards(value.provinceCode),
    enabled: hasProvince,
    staleTime: ADDRESS_CACHE_MS,
    gcTime: ADDRESS_CACHE_MS,
  });

  function handleSelectProvince(option: AddressOption) {
    // Chọn lại đúng tỉnh/thành đang chọn thì giữ phường/xã. Khách chỉ mở danh sách rồi chọn lại, không đổi ý.
    if (option.code === value.provinceCode) return;
    // Phường/xã cũ thuộc tỉnh/thành cũ nên không còn hợp lệ. Xóa để đơn không lưu cặp tỉnh và phường sai nhau.
    onChange({
      ...value,
      provinceCode: option.code,
      provinceName: option.name,
      wardCode: '',
      wardName: '',
    });
  }

  function handleSelectWard(option: AddressOption) {
    onChange({ ...value, wardCode: option.code, wardName: option.name });
  }

  const selectedProvince = hasProvince
    ? { code: value.provinceCode, name: value.provinceName }
    : null;
  const selectedWard =
    value.wardCode !== '' ? { code: value.wardCode, name: value.wardName } : null;

  return (
    <div className="address-picker">
      <div className="form-grid form-grid-2">
        <AddressCombobox
          id={provinceId}
          label="Tỉnh/Thành phố"
          options={provincesQuery.data ?? []}
          selected={selectedProvince}
          onSelect={handleSelectProvince}
          // Tải lỗi thì danh sách trống không giúp khách chọn. Khóa ô để lời báo lỗi và nút "Thử lại" là việc duy nhất làm được.
          disabled={disabled || provincesQuery.isError}
          isLoading={provincesQuery.isLoading}
          errorMessage={errors.province}
          statusSlot={
            provincesQuery.isError && (
              <LoadError
                message="Không tải được danh sách tỉnh/thành phố."
                reason={provincesQuery.error}
                onRetry={() => void provincesQuery.refetch()}
              />
            )
          }
        />
        <AddressCombobox
          id={wardId}
          label="Phường/Xã"
          options={wardsQuery.data ?? []}
          selected={selectedWard}
          onSelect={handleSelectWard}
          // Chưa có tỉnh/thành thì chưa có danh sách phường/xã để chọn.
          disabled={disabled || !hasProvince || wardsQuery.isError}
          isLoading={hasProvince && wardsQuery.isLoading}
          errorMessage={errors.ward}
          placeholder={hasProvince ? 'Chọn hoặc gõ để tìm' : 'Chọn tỉnh/thành phố trước'}
          statusSlot={
            hasProvince &&
            wardsQuery.isError && (
              <LoadError
                message="Không tải được danh sách phường/xã."
                reason={wardsQuery.error}
                onRetry={() => void wardsQuery.refetch()}
              />
            )
          }
        />
        <div className="field field-full">
          <label htmlFor={lineId}>Số nhà, tên đường</label>
          <input
            id={lineId}
            name="addressLine"
            type="text"
            autoComplete="address-line1"
            disabled={disabled}
            value={value.line}
            onChange={(event) => onChange({ ...value, line: event.target.value })}
            {...fieldErrorProps(lineId, errors.line)}
          />
          <FieldError inputId={lineId} message={errors.line} />
        </div>
      </div>
    </div>
  );
}

interface LoadErrorProps {
  message: string;
  reason: unknown;
  onRetry: () => void;
}

// Lỗi tải danh sách nằm ngay dưới ô cần danh sách đó, kèm nút thử lại.
function LoadError({ message, reason, onRetry }: LoadErrorProps) {
  // Câu lỗi của API đã là tiếng Việt. Lỗi lạ thì chỉ hiện câu chung, không lộ chữ kỹ thuật cho khách.
  const reasons = reason instanceof Error && reason.message ? [reason.message] : [];
  return (
    <div className="address-picker-load-error">
      <ErrorAlert message={message} items={reasons} />
      <button type="button" className="btn btn-outline btn-sm" onClick={onRetry}>
        Thử lại
      </button>
    </div>
  );
}
