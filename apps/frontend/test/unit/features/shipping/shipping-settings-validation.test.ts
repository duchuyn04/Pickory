import { describe, expect, it } from 'vitest';
import { ApiError } from '../../../../src/shared/api';
import {
  mapServerErrors,
  validateShippingForm,
  type ShippingFormValues,
} from '../../../../src/features/shipping/shipping-settings-validation';

const validValues: ShippingFormValues = {
  senderName: 'Kho Pickory',
  senderPhone: '0912345678',
  provinceCode: '01',
  wardCode: '0101',
  line: '12 Phố Test',
};

describe('validateShippingForm', () => {
  it('giá trị hợp lệ thì không có lỗi', () => {
    expect(validateShippingForm(validValues)).toEqual({});
  });

  it('mọi ô bắt buộc trống thì mỗi ô một lỗi riêng', () => {
    const errors = validateShippingForm({
      senderName: '',
      senderPhone: '',
      provinceCode: '',
      wardCode: '',
      line: '',
    });

    expect(errors).toEqual({
      senderName: 'Cần nhập tên người gửi.',
      senderPhone: 'Cần nhập số điện thoại người gửi.',
      province: 'Cần chọn tỉnh/thành phố.',
      ward: 'Cần chọn phường/xã.',
      line: 'Cần nhập số nhà, tên đường.',
    });
  });

  it('chỉ có khoảng trắng thì tính là trống', () => {
    const errors = validateShippingForm({ ...validValues, senderName: '   ', line: '  ' });

    expect(errors.senderName).toBe('Cần nhập tên người gửi.');
    expect(errors.line).toBe('Cần nhập số nhà, tên đường.');
  });

  it('số điện thoại sai định dạng thì báo đúng ô số điện thoại', () => {
    const errors = validateShippingForm({ ...validValues, senderPhone: '0212345678' });

    expect(errors).toEqual({ senderPhone: 'Số điện thoại không đúng định dạng.' });
  });

  it('số điện thoại dạng +84 có khoảng trắng vẫn hợp lệ', () => {
    expect(validateShippingForm({ ...validValues, senderPhone: '+84 912 345 678' })).toEqual({});
  });

  it('tên quá 100 ký tự hoặc có ký tự điều khiển thì báo lỗi ở ô tên', () => {
    expect(validateShippingForm({ ...validValues, senderName: 'a'.repeat(101) }).senderName).toBe(
      'Họ và tên tối đa 100 ký tự.',
    );
    expect(validateShippingForm({ ...validValues, senderName: 'Kho\nPickory' }).senderName).toBe(
      'Họ và tên chứa ký tự không hợp lệ.',
    );
  });

  it('số nhà đúng 200 ký tự hợp lệ, 201 ký tự thì lỗi', () => {
    expect(validateShippingForm({ ...validValues, line: 'a'.repeat(200) })).toEqual({});
    expect(validateShippingForm({ ...validValues, line: 'a'.repeat(201) }).line).toBe(
      'Số nhà, tên đường tối đa 200 ký tự.',
    );
  });

  it('số nhà có ký tự điều khiển thì lỗi', () => {
    expect(validateShippingForm({ ...validValues, line: '12\tPhố' }).line).toBe(
      'Số nhà, tên đường chứa ký tự không hợp lệ.',
    );
  });
});

type Detail = { key: string; message: string };

function validationError(details?: Detail[], message = 'Dữ liệu không hợp lệ.') {
  return new ApiError('VALIDATION_ERROR', 400, message, details);
}

describe('mapServerErrors', () => {
  it('gán từng khóa của details vào đúng ô của form', () => {
    const error = validationError([
      { key: 'provinceCode', message: 'Tỉnh/thành không có trong danh mục hoặc đã ngừng dùng.' },
      { key: 'wardCode', message: 'Phường/xã không thuộc tỉnh/thành đã chọn.' },
      { key: 'line', message: 'Địa chỉ chi tiết tối đa 200 ký tự.' },
      { key: 'senderName', message: 'Cần nhập tên người gửi.' },
      { key: 'senderPhone', message: 'Số điện thoại không đúng định dạng.' },
      { key: 'defaultServiceTypeId', message: 'Gói dịch vụ mặc định phải là 2, 5 hoặc để trống.' },
    ]);

    const result = mapServerErrors(error);

    expect(result.fieldErrors).toEqual({
      province: 'Tỉnh/thành không có trong danh mục hoặc đã ngừng dùng.',
      ward: 'Phường/xã không thuộc tỉnh/thành đã chọn.',
      line: 'Địa chỉ chi tiết tối đa 200 ký tự.',
      senderName: 'Cần nhập tên người gửi.',
      senderPhone: 'Số điện thoại không đúng định dạng.',
      serviceType: 'Gói dịch vụ mặc định phải là 2, 5 hoặc để trống.',
    });
    expect(result.unmatched).toEqual([]);
  });

  it('khóa không thuộc form thì vào danh sách không gắn được', () => {
    const error = validationError([
      { key: 'provinceName', message: 'Trường này không được hỗ trợ.' },
      { key: 'wardCode', message: 'Cần chọn phường/xã.' },
    ]);

    const result = mapServerErrors(error);

    expect(result.fieldErrors).toEqual({ ward: 'Cần chọn phường/xã.' });
    expect(result.unmatched).toEqual(['Trường này không được hỗ trợ.']);
  });

  it('lỗi cả body có khóa body thì hiện nguyên thông báo ở danh sách không gắn được', () => {
    const error = validationError([
      { key: 'body', message: 'Dữ liệu gửi lên phải là một đối tượng JSON.' },
    ]);

    const result = mapServerErrors(error);

    expect(result.fieldErrors).toEqual({});
    expect(result.unmatched).toEqual(['Dữ liệu gửi lên phải là một đối tượng JSON.']);
  });

  it('không đọc ngược câu message: thiếu details thì giữ nguyên câu ở danh sách không gắn được', () => {
    const message = 'Dữ liệu không hợp lệ (wardCode: Phường/xã không thuộc tỉnh/thành đã chọn.).';

    const result = mapServerErrors(validationError(undefined, message));

    expect(result.fieldErrors).toEqual({});
    expect(result.unmatched).toEqual([message]);
  });

  it('lỗi không phải kiểu kiểm tra dữ liệu thì giữ nguyên thông báo', () => {
    const error = new ApiError('INTERNAL_ERROR', 500, 'Đã có lỗi xảy ra, vui lòng thử lại sau.');

    const result = mapServerErrors(error);

    expect(result.fieldErrors).toEqual({});
    expect(result.unmatched).toEqual(['Đã có lỗi xảy ra, vui lòng thử lại sau.']);
  });
});
