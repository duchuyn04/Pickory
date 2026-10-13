import { describe, expect, it } from 'vitest';
import {
  EMPTY_ADDRESS_FORM,
  mapAddressServerErrors,
  toAddressBody,
  validateAddressForm,
  type AddressFormValues,
} from '../../../../../../src/features/commerce/customer/addresses/address-form';

const valid: AddressFormValues = {
  label: 'Nhà',
  name: 'Nguyễn Văn An',
  phone: '0912345678',
  address: {
    provinceCode: '201',
    provinceName: 'Hà Nội',
    wardCode: '1A0101',
    wardName: 'Phường Ba Đình',
    line: '12 Phố Test',
  },
  isDefault: false,
};

describe('validateAddressForm', () => {
  it('nhận form hợp lệ', () => {
    expect(validateAddressForm(valid)).toEqual({});
  });

  it('báo lỗi từng ô trống của form mới', () => {
    const errors = validateAddressForm(EMPTY_ADDRESS_FORM);

    expect(Object.keys(errors).sort()).toEqual([
      'address.line',
      'address.provinceCode',
      'address.wardCode',
      'label',
      'name',
      'phone',
    ]);
  });

  it('từ chối nhãn chỉ có khoảng trắng và nhãn dài quá 50 ký tự', () => {
    expect(validateAddressForm({ ...valid, label: '   ' }).label).toBe('Cần nhập nhãn địa chỉ.');
    expect(validateAddressForm({ ...valid, label: 'a'.repeat(51) }).label).toBe(
      'Nhãn địa chỉ tối đa 50 ký tự.',
    );
    expect(validateAddressForm({ ...valid, label: 'a'.repeat(50) }).label).toBeUndefined();
  });

  it('từ chối số điện thoại sai định dạng', () => {
    expect(validateAddressForm({ ...valid, phone: '12345' }).phone).toBe(
      'Số điện thoại không đúng định dạng.',
    );
  });

  it('từ chối số nhà dài quá 200 ký tự', () => {
    const errors = validateAddressForm({
      ...valid,
      address: { ...valid.address, line: 'a'.repeat(201) },
    });

    expect(errors['address.line']).toBe('Số nhà, tên đường tối đa 200 ký tự.');
  });
});

describe('toAddressBody', () => {
  it('bỏ khoảng trắng hai đầu và chỉ gửi mã tỉnh, mã phường, số nhà', () => {
    expect(
      toAddressBody({
        ...valid,
        label: ' Nhà ',
        name: ' Nguyễn Văn An ',
        isDefault: true,
        address: { ...valid.address, line: ' 12 Phố Test ' },
      }),
    ).toEqual({
      label: 'Nhà',
      name: 'Nguyễn Văn An',
      phone: '0912345678',
      address: { provinceCode: '201', wardCode: '1A0101', line: '12 Phố Test' },
      isDefault: true,
    });
  });
});

describe('mapAddressServerErrors', () => {
  it('gán lỗi theo khóa của API vào đúng ô', () => {
    const { fieldErrors, unmatched } = mapAddressServerErrors([
      { key: 'label', message: 'Nhãn sai.' },
      { key: 'address.wardCode', message: 'Phường không thuộc tỉnh.' },
    ]);

    expect(fieldErrors).toEqual({
      label: 'Nhãn sai.',
      'address.wardCode': 'Phường không thuộc tỉnh.',
    });
    expect(unmatched).toEqual([]);
  });

  it('giữ lỗi không có ô tương ứng để hiện chung', () => {
    const { fieldErrors, unmatched } = mapAddressServerErrors([
      { key: '', message: 'Cần gửi ít nhất một trường.' },
      { key: 'khac', message: 'Lỗi lạ.' },
    ]);

    expect(fieldErrors).toEqual({});
    expect(unmatched).toEqual(['Cần gửi ít nhất một trường.', 'Lỗi lạ.']);
  });

  it('không có details thì không có lỗi', () => {
    expect(mapAddressServerErrors(undefined)).toEqual({ fieldErrors: {}, unmatched: [] });
  });
});
