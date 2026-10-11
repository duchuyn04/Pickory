import { describe, expect, it } from 'vitest';
import { ApiError } from '../../../../src/shared/api';
import {
  formatVnd,
  mapServerErrors,
  validateCommerceForm,
  type CommerceFormValues,
} from '../../../../src/features/commerce/commerce-settings-validation';

const validValues: CommerceFormValues = {
  freeShippingThreshold: '500000',
  codMaxAmount: '20000000',
  maxQuantityPerSku: '5',
  maxCartLines: '20',
  sepayAccountNumber: '0123456789',
  sepayBankCode: 'MB',
};

function withValue(field: keyof CommerceFormValues, value: string): CommerceFormValues {
  return { ...validValues, [field]: value };
}

describe('validateCommerceForm', () => {
  it('giá trị hợp lệ thì không có lỗi', () => {
    expect(validateCommerceForm(validValues)).toEqual({});
  });

  it('hai ô SePay cùng trống thì hợp lệ', () => {
    const values = { ...validValues, sepayAccountNumber: '', sepayBankCode: '   ' };

    expect(validateCommerceForm(values)).toEqual({});
  });

  it.each([
    ['freeShippingThreshold', '0', false],
    ['freeShippingThreshold', '1000000000', false],
    ['freeShippingThreshold', '-1', true],
    ['freeShippingThreshold', '1000000001', true],
    ['codMaxAmount', '0', false],
    ['codMaxAmount', '1000000000', false],
    ['codMaxAmount', '-1', true],
    ['codMaxAmount', '1000000001', true],
    ['maxQuantityPerSku', '1', false],
    ['maxQuantityPerSku', '99', false],
    ['maxQuantityPerSku', '0', true],
    ['maxQuantityPerSku', '100', true],
    ['maxCartLines', '1', false],
    ['maxCartLines', '100', false],
    ['maxCartLines', '0', true],
    ['maxCartLines', '101', true],
  ] as const)('biên của %s: giá trị %s, có lỗi là %s', (field, value, hasError) => {
    const errors = validateCommerceForm(withValue(field, value));

    expect(Boolean(errors[field])).toBe(hasError);
    expect(Object.keys(errors)).toEqual(hasError ? [field] : []);
  });

  it.each(['', '  ', '5.5', 'abc', '1e3', '5 0', '+5'])('ô số nhận "%s" thì báo lỗi', (value) => {
    const errors = validateCommerceForm(withValue('maxCartLines', value));

    expect(errors.maxCartLines).toBe('Số dòng tối đa mỗi giỏ phải là số nguyên từ 1 đến 100.');
  });

  it('thông báo lỗi nêu đúng khoảng của từng ô số', () => {
    const errors = validateCommerceForm({
      ...validValues,
      freeShippingThreshold: '-1',
      codMaxAmount: '-1',
      maxQuantityPerSku: '0',
      maxCartLines: '0',
    });

    expect(errors).toEqual({
      freeShippingThreshold:
        'Ngưỡng miễn phí giao hàng phải là số nguyên từ 0 đến 1.000.000.000 đ.',
      codMaxAmount: 'Hạn mức COD phải là số nguyên từ 0 đến 1.000.000.000 đ.',
      maxQuantityPerSku: 'Số lượng tối đa mỗi sản phẩm phải là số nguyên từ 1 đến 99.',
      maxCartLines: 'Số dòng tối đa mỗi giỏ phải là số nguyên từ 1 đến 100.',
    });
  });

  it.each(['12345', '123456789012345678901', '12345a', '123 456', '-123456'])(
    'số tài khoản "%s" sai định dạng',
    (value) => {
      const errors = validateCommerceForm(withValue('sepayAccountNumber', value));

      expect(errors).toEqual({ sepayAccountNumber: 'Số tài khoản gồm từ 6 đến 20 chữ số.' });
    },
  );

  it.each(['123456', '12345678901234567890', '  0123456789  '])(
    'số tài khoản "%s" hợp lệ',
    (value) => {
      expect(validateCommerceForm(withValue('sepayAccountNumber', value))).toEqual({});
    },
  );

  it.each(['M', 'AAAAAAAAAAAAAAAAAAAAA', 'MB-1', 'M B', 'ÂB'])(
    'mã ngân hàng "%s" sai định dạng',
    (value) => {
      const errors = validateCommerceForm(withValue('sepayBankCode', value));

      expect(errors).toEqual({
        sepayBankCode: 'Mã ngân hàng gồm từ 2 đến 20 chữ cái hoặc chữ số.',
      });
    },
  );

  it.each(['MB', 'mb', ' vcb ', 'ACB2'])('mã ngân hàng "%s" hợp lệ', (value) => {
    expect(validateCommerceForm(withValue('sepayBankCode', value))).toEqual({});
  });

  it('chỉ nhập số tài khoản thì lỗi nằm ở ô mã ngân hàng', () => {
    const errors = validateCommerceForm(withValue('sepayBankCode', ''));

    expect(errors).toEqual({
      sepayBankCode: 'Cần nhập cả số tài khoản và mã ngân hàng, hoặc để trống cả hai.',
    });
  });

  it('chỉ nhập mã ngân hàng thì lỗi nằm ở ô số tài khoản', () => {
    const errors = validateCommerceForm(withValue('sepayAccountNumber', ''));

    expect(errors).toEqual({
      sepayAccountNumber: 'Cần nhập cả số tài khoản và mã ngân hàng, hoặc để trống cả hai.',
    });
  });
});

describe('formatVnd', () => {
  it('nhóm hàng nghìn bằng dấu chấm và thêm đơn vị đ', () => {
    expect(formatVnd(500000)).toBe('500.000 đ');
    expect(formatVnd(20000000)).toBe('20.000.000 đ');
    expect(formatVnd(0)).toBe('0 đ');
  });
});

describe('mapServerErrors', () => {
  it('gán lỗi của từng trường vào đúng ô theo khóa trong details', () => {
    const error = new ApiError('VALIDATION_ERROR', 400, 'Dữ liệu không hợp lệ.', [
      { key: 'codMaxAmount', message: 'codMaxAmount phải là số nguyên từ 0 đến 1000000000.' },
      { key: 'sepayBankCode', message: 'Cần nhập cả số tài khoản và mã ngân hàng.' },
    ]);

    const mapped = mapServerErrors(error);

    expect(mapped.fieldErrors).toEqual({
      codMaxAmount: 'Hạn mức COD phải là số nguyên từ 0 đến 1.000.000.000 đ.',
      sepayBankCode: 'Cần nhập cả số tài khoản và mã ngân hàng.',
    });
    expect(mapped.unmatched).toEqual([]);
  });

  it('khóa lạ và lỗi cả body vào danh sách không gắn được', () => {
    const error = new ApiError('VALIDATION_ERROR', 400, 'Dữ liệu không hợp lệ.', [
      { key: 'sepayWebhookApiKey', message: 'Trường này không được hỗ trợ.' },
      { key: 'body', message: 'Dữ liệu gửi lên phải là một đối tượng JSON.' },
    ]);

    const mapped = mapServerErrors(error);

    expect(mapped.fieldErrors).toEqual({});
    expect(mapped.unmatched).toEqual([
      'Trường này không được hỗ trợ.',
      'Dữ liệu gửi lên phải là một đối tượng JSON.',
    ]);
  });

  it('không có details thì hiện nguyên câu message', () => {
    const error = new ApiError('INTERNAL_ERROR', 500, 'Máy chủ đang bận.');

    expect(mapServerErrors(error)).toEqual({
      fieldErrors: {},
      unmatched: ['Máy chủ đang bận.'],
    });
  });
});
