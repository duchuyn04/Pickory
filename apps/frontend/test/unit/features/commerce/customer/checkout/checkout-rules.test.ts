import { describe, expect, it } from 'vitest';
import { ApiError } from '../../../../../../src/shared/api';
import {
  buildPreviewInput,
  describeOrderError,
  describePreviewError,
  firstErrorField,
  mapServerDetailsToFieldErrors,
} from '../../../../../../src/features/commerce/customer/checkout/checkout-rules';

const completeAddress = { provinceCode: '201', wardCode: '1A0101', line: '12 Phố Test' };

describe('buildPreviewInput: điều kiện gọi preview', () => {
  it('trả dữ liệu gửi đi khi địa chỉ đủ tỉnh, phường và số nhà', () => {
    expect(buildPreviewInput(completeAddress, 'cod')).toEqual({
      address: completeAddress,
      paymentMethod: 'cod',
    });
  });

  it.each([
    ['thiếu tỉnh', { ...completeAddress, provinceCode: '' }],
    ['thiếu phường', { ...completeAddress, wardCode: '' }],
    ['thiếu số nhà', { ...completeAddress, line: '' }],
    ['số nhà chỉ có khoảng trắng', { ...completeAddress, line: '   ' }],
    ['số nhà dài hơn 200 ký tự', { ...completeAddress, line: 'a'.repeat(201) }],
  ])('không gọi preview khi %s', (_name, address) => {
    expect(buildPreviewInput(address, 'cod')).toBeNull();
  });

  it('cắt khoảng trắng của số nhà và chỉ gửi đúng 3 trường địa chỉ', () => {
    const input = buildPreviewInput({ ...completeAddress, line: '  12 Phố Test  ' }, 'sepay');
    expect(input?.address).toEqual(completeAddress);
    expect(Object.keys(input?.address ?? {}).sort()).toEqual(['line', 'provinceCode', 'wardCode']);
  });
});

describe('mapServerDetailsToFieldErrors', () => {
  it('gán lỗi vào đúng ô theo key và gom key lạ vào danh sách chưa gán', () => {
    const result = mapServerDetailsToFieldErrors([
      { key: 'contact.phone', message: 'Số điện thoại không đúng định dạng.' },
      { key: 'invoice.taxCode', message: 'Mã số thuế sai.' },
      { key: 'address', message: 'Địa chỉ không còn trong danh mục.' },
      { key: 'cart', message: 'Giỏ hàng đang trống.' },
    ]);
    expect(result.fieldErrors).toEqual({
      'contact.phone': 'Số điện thoại không đúng định dạng.',
      'invoice.taxCode': 'Mã số thuế sai.',
      address: 'Địa chỉ không còn trong danh mục.',
    });
    expect(result.unmatched).toEqual(['Giỏ hàng đang trống.']);
  });

  it('giữ lỗi đầu tiên khi một ô có nhiều lỗi', () => {
    const result = mapServerDetailsToFieldErrors([
      { key: 'note', message: 'Lỗi thứ nhất.' },
      { key: 'note', message: 'Lỗi thứ hai.' },
    ]);
    expect(result.fieldErrors.note).toBe('Lỗi thứ nhất.');
  });

  it('trả rỗng khi không có details', () => {
    expect(mapServerDetailsToFieldErrors(undefined)).toEqual({ fieldErrors: {}, unmatched: [] });
  });
});

describe('firstErrorField', () => {
  it('trả ô lỗi đầu tiên theo thứ tự hiện trên form', () => {
    expect(
      firstErrorField({ 'invoice.email': 'x', 'address.line': 'x', 'contact.email': 'x' }),
    ).toBe('contact.email');
  });

  it('trả null khi không có lỗi', () => {
    expect(firstErrorField({ note: null })).toBeNull();
  });
});

describe('describePreviewError', () => {
  it.each(['SHIPPING_UNAVAILABLE', 'SHIPPING_NOT_CONFIGURED'])(
    'mã %s thành lỗi phí vận chuyển với đúng câu',
    (code) => {
      const failure = describePreviewError(new ApiError(code, 503, 'Gián đoạn'));
      expect(failure).toEqual({
        kind: 'shipping-unavailable',
        message: 'Chưa tính được phí vận chuyển, thử lại',
      });
    },
  );

  it('mã COD_NOT_ALLOWED thành lỗi hạn mức COD kèm câu của API', () => {
    const failure = describePreviewError(new ApiError('COD_NOT_ALLOWED', 422, 'Vượt hạn mức.'));
    expect(failure).toEqual({ kind: 'cod-not-allowed', message: 'Vượt hạn mức.' });
  });

  it('lỗi 400 có key address thành lỗi dưới ô địa chỉ', () => {
    const failure = describePreviewError(
      new ApiError('VALIDATION_ERROR', 400, 'Dữ liệu không hợp lệ.', [
        { key: 'address', message: 'Địa chỉ không còn trong danh mục.' },
      ]),
    );
    expect(failure).toEqual({
      kind: 'address',
      fieldErrors: { address: 'Địa chỉ không còn trong danh mục.' },
    });
  });

  it('SKU_UNAVAILABLE và giỏ trống dẫn về giỏ hàng', () => {
    expect(describePreviewError(new ApiError('SKU_UNAVAILABLE', 422, 'Có sản phẩm hết.'))).toEqual({
      kind: 'cart-problem',
      message: 'Có sản phẩm hết.',
    });
    expect(
      describePreviewError(
        new ApiError('VALIDATION_ERROR', 400, 'Dữ liệu không hợp lệ.', [
          { key: 'cart', message: 'Giỏ hàng đang trống.' },
        ]),
      ),
    ).toEqual({ kind: 'cart-problem', message: 'Giỏ hàng đang trống.' });
  });

  it('TOO_MANY_REQUESTS giữ câu của API', () => {
    expect(describePreviewError(new ApiError('TOO_MANY_REQUESTS', 429, 'Thử lại sau.'))).toEqual({
      kind: 'rate-limited',
      message: 'Thử lại sau.',
    });
  });

  it('lỗi lạ giữ câu của API', () => {
    expect(describePreviewError(new ApiError('NETWORK_ERROR', 0, 'Không kết nối được.'))).toEqual({
      kind: 'other',
      message: 'Không kết nối được.',
    });
  });
});

describe('describeOrderError', () => {
  it('ORDER_TOTAL_CHANGED thành yêu cầu xem lại tổng', () => {
    expect(describeOrderError(new ApiError('ORDER_TOTAL_CHANGED', 409, 'Giá đã đổi.'))).toEqual({
      kind: 'total-changed',
      message: 'Giá đã đổi.',
    });
  });

  it('INSUFFICIENT_STOCK giữ danh sách SKU thiếu theo details', () => {
    const failure = describeOrderError(
      new ApiError('INSUFFICIENT_STOCK', 409, 'Không đủ hàng.', [
        { key: 'sku-1', message: 'Sản phẩm này đã hết hàng.' },
      ]),
    );
    expect(failure).toEqual({
      kind: 'insufficient-stock',
      message: 'Không đủ hàng.',
      shortages: [{ skuId: 'sku-1', message: 'Sản phẩm này đã hết hàng.' }],
    });
  });

  it('lỗi 400 gán vào ô theo key, key lạ vào danh sách chưa gán', () => {
    const failure = describeOrderError(
      new ApiError('VALIDATION_ERROR', 400, 'Dữ liệu không hợp lệ.', [
        { key: 'contact.name', message: 'Cần nhập họ và tên.' },
        { key: 'expectedTotal', message: 'Tổng tiền dự kiến sai.' },
      ]),
    );
    expect(failure).toEqual({
      kind: 'field-errors',
      fieldErrors: { 'contact.name': 'Cần nhập họ và tên.' },
      unmatched: ['Tổng tiền dự kiến sai.'],
    });
  });

  it('mã khác dùng câu của API', () => {
    expect(describeOrderError(new ApiError('SERVER_ERROR', 500, 'Lỗi máy chủ.'))).toEqual({
      kind: 'other',
      message: 'Lỗi máy chủ.',
    });
  });
});
