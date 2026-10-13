import type { FieldErrors } from '../../../../shared/field-error';
import { getNameError, getPhoneError } from '../../../../shared/identity-validation';
import { EMPTY_ADDRESS, type AddressValue } from '../../../shipping/address-picker';
import { ADDRESS_LINE_MAX_LENGTH } from '../checkout/checkout-form-schema';
import type { SavedAddressBody } from './addresses-api';

// Nhãn dài tối đa 50 ký tự. Khớp `SavedAddressRequests` của backend.
export const LABEL_MAX_LENGTH = 50;

// Tên ô trùng với `key` mà backend trả trong `error.details`, nên lỗi từ server gán thẳng vào ô.
export type AddressFieldName =
  | 'label'
  | 'name'
  | 'phone'
  | 'address.provinceCode'
  | 'address.wardCode'
  | 'address.line'
  | 'isDefault';

export type AddressFieldErrors = FieldErrors<AddressFieldName>;

export interface AddressFormValues {
  label: string;
  name: string;
  phone: string;
  address: AddressValue;
  isDefault: boolean;
}

export const EMPTY_ADDRESS_FORM: AddressFormValues = {
  label: '',
  name: '',
  phone: '',
  address: EMPTY_ADDRESS,
  isDefault: false,
};

// Kiểm form với cùng luật của backend, để lỗi hiện ngay dưới ô trước khi gửi request.
export function validateAddressForm(values: AddressFormValues): AddressFieldErrors {
  const errors: AddressFieldErrors = {};

  const label = values.label.trim();
  if (label === '') {
    errors.label = 'Cần nhập nhãn địa chỉ.';
  } else if (label.length > LABEL_MAX_LENGTH) {
    errors.label = `Nhãn địa chỉ tối đa ${LABEL_MAX_LENGTH} ký tự.`;
  }

  // Tên và số điện thoại bắt buộc ở sổ địa chỉ, dù luật chung của hồ sơ cho phép để trống.
  if (values.name.trim() === '') {
    errors.name = 'Cần nhập họ và tên.';
  } else {
    const nameError = getNameError(values.name);
    if (nameError) errors.name = nameError;
  }

  if (values.phone.trim() === '') {
    errors.phone = 'Cần nhập số điện thoại.';
  } else {
    const phoneError = getPhoneError(values.phone);
    if (phoneError) errors.phone = phoneError;
  }

  if (values.address.provinceCode === '')
    errors['address.provinceCode'] = 'Cần chọn tỉnh/thành phố.';
  if (values.address.wardCode === '') errors['address.wardCode'] = 'Cần chọn phường/xã.';

  const line = values.address.line.trim();
  if (line === '') {
    errors['address.line'] = 'Cần nhập số nhà, tên đường.';
  } else if (line.length > ADDRESS_LINE_MAX_LENGTH) {
    errors['address.line'] = `Số nhà, tên đường tối đa ${ADDRESS_LINE_MAX_LENGTH} ký tự.`;
  }

  return errors;
}

// Body gửi API. Tên tỉnh và tên phường không gửi vì backend tra lại từ mã.
export function toAddressBody(values: AddressFormValues): SavedAddressBody {
  return {
    label: values.label.trim(),
    name: values.name.trim(),
    phone: values.phone.trim(),
    address: {
      provinceCode: values.address.provinceCode,
      wardCode: values.address.wardCode,
      line: values.address.line.trim(),
    },
    isDefault: values.isDefault,
  };
}

const KNOWN_FIELDS: AddressFieldName[] = [
  'label',
  'name',
  'phone',
  'address.provinceCode',
  'address.wardCode',
  'address.line',
  'isDefault',
];

// Gán từng phần tử `details` của API vào ô cùng tên. Lỗi không có ô tương ứng giữ lại để hiện chung.
export function mapAddressServerErrors(details: { key: string; message: string }[] | undefined): {
  fieldErrors: AddressFieldErrors;
  unmatched: string[];
} {
  const fieldErrors: AddressFieldErrors = {};
  const unmatched: string[] = [];
  for (const detail of details ?? []) {
    const field = KNOWN_FIELDS.find((name) => name === detail.key);
    if (field === undefined) {
      unmatched.push(detail.message);
    } else if (!fieldErrors[field]) {
      fieldErrors[field] = detail.message;
    }
  }
  return { fieldErrors, unmatched };
}
