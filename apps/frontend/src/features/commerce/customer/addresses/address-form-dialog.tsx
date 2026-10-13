import { useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { AdminFormDialog } from '../../../../shared/admin-form-dialog';
import { ApiError } from '../../../../shared/api';
import { FieldError, fieldErrorProps } from '../../../../shared/field-error';
import { AddressPicker } from '../../../shipping/address-picker';
import {
  MY_ADDRESSES_KEY,
  createSavedAddress,
  updateSavedAddress,
  type SavedAddress,
} from './addresses-api';
import {
  EMPTY_ADDRESS_FORM,
  LABEL_MAX_LENGTH,
  mapAddressServerErrors,
  toAddressBody,
  validateAddressForm,
  type AddressFieldErrors,
  type AddressFormValues,
} from './address-form';

const ID_PREFIX = 'book';

interface AddressFormDialogProps {
  // Địa chỉ đang sửa. `null` là thêm địa chỉ mới.
  editing: SavedAddress | null;
  // Địa chỉ đầu tiên tự là mặc định ở backend, nên ô tích chỉ hỏi khi sổ đã có địa chỉ.
  isFirstAddress: boolean;
  onClose: () => void;
}

function valuesOf(address: SavedAddress | null): AddressFormValues {
  if (address === null) return EMPTY_ADDRESS_FORM;
  return {
    label: address.label,
    name: address.name,
    phone: address.phone,
    address: address.address,
    isDefault: address.isDefault,
  };
}

// Hộp thoại thêm hoặc sửa một địa chỉ trong sổ. Lỗi theo trường của API hiện dưới đúng ô.
export function AddressFormDialog({ editing, isFirstAddress, onClose }: AddressFormDialogProps) {
  const queryClient = useQueryClient();
  const [values, setValues] = useState<AddressFormValues>(() => valuesOf(editing));
  const [fieldErrors, setFieldErrors] = useState<AddressFieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  function clearError(field: keyof AddressFieldErrors) {
    // Sửa ô nào thì lỗi của ô đó hết hiệu lực, vì lỗi cũ nói về giá trị cũ.
    setFieldErrors((current) => ({ ...current, [field]: null }));
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    // Bấm lần hai trong lúc chờ API không được gửi thêm request.
    if (isSubmitting) return;
    setApiError(null);

    const errors = validateAddressForm(values);
    setFieldErrors(errors);
    // Form sai dừng tại đây, để không gửi request chắc chắn bị backend từ chối.
    if (Object.keys(errors).length > 0) return;

    setIsSubmitting(true);
    try {
      const body = toAddressBody(values);
      if (editing) {
        await updateSavedAddress(editing.id, body);
      } else {
        await createSavedAddress(body);
      }
      // Sổ đổi nên danh sách ở trang tài khoản và ô chọn ở checkout cùng tải lại.
      await queryClient.invalidateQueries({ queryKey: MY_ADDRESSES_KEY });
      onClose();
    } catch (error: unknown) {
      setIsSubmitting(false);
      if (error instanceof ApiError && error.details && error.details.length > 0) {
        const { fieldErrors: serverErrors, unmatched } = mapAddressServerErrors(error.details);
        setFieldErrors(serverErrors);
        // Lỗi không gắn được vào ô nào thì hiện chung, để khách không mất thông tin.
        setApiError(unmatched.length > 0 ? unmatched.join(' ') : null);
        return;
      }
      // Lỗi 422 đủ 10 địa chỉ và lỗi khác đã có thông báo tiếng Việt nên hiện nguyên văn.
      setApiError(error instanceof Error ? error.message : 'Lưu địa chỉ thất bại.');
    }
  }

  return (
    <AdminFormDialog
      isOpen
      onClose={onClose}
      title={editing ? 'Sửa địa chỉ' : 'Thêm địa chỉ'}
      formId="address-form"
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitText="Lưu địa chỉ"
      error={apiError}
    >
      <div className="field">
        <label htmlFor={`${ID_PREFIX}-label`}>Nhãn địa chỉ</label>
        <input
          id={`${ID_PREFIX}-label`}
          type="text"
          list="address-label-suggestions"
          maxLength={LABEL_MAX_LENGTH + 20}
          placeholder="Nhà, Công ty..."
          value={values.label}
          onChange={(event) => {
            setValues((current) => ({ ...current, label: event.target.value }));
            clearError('label');
          }}
          {...fieldErrorProps(`${ID_PREFIX}-label`, fieldErrors.label)}
        />
        <datalist id="address-label-suggestions">
          <option value="Nhà" />
          <option value="Công ty" />
        </datalist>
        <FieldError inputId={`${ID_PREFIX}-label`} message={fieldErrors.label} />
      </div>
      <div className="form-grid form-grid-2">
        <div className="field">
          <label htmlFor={`${ID_PREFIX}-name`}>Họ và tên người nhận</label>
          <input
            id={`${ID_PREFIX}-name`}
            type="text"
            autoComplete="name"
            value={values.name}
            onChange={(event) => {
              setValues((current) => ({ ...current, name: event.target.value }));
              clearError('name');
            }}
            {...fieldErrorProps(`${ID_PREFIX}-name`, fieldErrors.name)}
          />
          <FieldError inputId={`${ID_PREFIX}-name`} message={fieldErrors.name} />
        </div>
        <div className="field">
          <label htmlFor={`${ID_PREFIX}-phone`}>Số điện thoại</label>
          <input
            id={`${ID_PREFIX}-phone`}
            type="tel"
            autoComplete="tel"
            inputMode="tel"
            value={values.phone}
            onChange={(event) => {
              setValues((current) => ({ ...current, phone: event.target.value }));
              clearError('phone');
            }}
            {...fieldErrorProps(`${ID_PREFIX}-phone`, fieldErrors.phone)}
          />
          <FieldError inputId={`${ID_PREFIX}-phone`} message={fieldErrors.phone} />
        </div>
      </div>

      <AddressPicker
        value={values.address}
        onChange={(next) => {
          setValues((current) => ({ ...current, address: next }));
          setFieldErrors((current) => ({
            ...current,
            'address.provinceCode': null,
            'address.wardCode': null,
            'address.line': null,
          }));
        }}
        idPrefix={ID_PREFIX}
        errors={{
          province: fieldErrors['address.provinceCode'],
          ward: fieldErrors['address.wardCode'],
          line: fieldErrors['address.line'],
        }}
      />

      {/* Địa chỉ đầu tiên của sổ tự là mặc định, nên ô tích chỉ hiện từ địa chỉ thứ hai. */}
      {(!isFirstAddress || editing !== null) && (
        <label className="check">
          <input
            type="checkbox"
            checked={values.isDefault}
            onChange={(event) =>
              setValues((current) => ({ ...current, isDefault: event.target.checked }))
            }
          />
          Đặt làm địa chỉ mặc định
        </label>
      )}
    </AdminFormDialog>
  );
}
