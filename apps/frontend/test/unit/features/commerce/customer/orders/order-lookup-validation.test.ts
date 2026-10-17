import { describe, expect, it } from 'vitest';
import {
  getLookupEmailError,
  getOrderCodeError,
  normalizeOrderCode,
} from '../../../../../../src/features/commerce/customer/orders/order-lookup-validation';

describe('chuẩn hóa mã đơn tra cứu', () => {
  it('cắt khoảng trắng hai đầu và đổi sang chữ hoa', () => {
    expect(normalizeOrderCode('  pkab23cd45 ')).toBe('PKAB23CD45');
  });
});

describe('kiểm mã đơn tra cứu', () => {
  it.each(['', '   '])('báo cần nhập khi mã đơn trống %j', (code) => {
    expect(getOrderCodeError(code)).toBe('Cần nhập mã đơn hàng.');
  });

  it('chấp nhận mã đơn viết thường có khoảng trắng hai đầu', () => {
    expect(getOrderCodeError(' pkab23cd45 ')).toBeNull();
  });

  it.each([
    ['quá ngắn', 'PKAB23CD4'],
    ['quá dài', 'PKAB23CD456'],
    ['sai tiền tố', 'XXAB23CD45'],
    ['chứa chữ O không có trong bảng chữ', 'PKAB23CDO5'],
    ['chứa chữ I không có trong bảng chữ', 'PKAB23CDI5'],
    ['chứa chữ số 0 không có trong bảng chữ', 'PKAB23CD05'],
    ['chứa chữ số 1 không có trong bảng chữ', 'PKAB23CD15'],
    ['chứa ký tự đặc biệt', 'PKAB23CD4-'],
  ])('báo sai định dạng khi mã đơn %s', (_reason, code) => {
    expect(getOrderCodeError(code)).toBe('Mã đơn hàng không đúng định dạng.');
  });
});

describe('kiểm email tra cứu', () => {
  it.each(['', '   '])('báo cần nhập khi email trống %j', (email) => {
    expect(getLookupEmailError(email)).toBe('Cần nhập email.');
  });

  it.each(['abc', 'abc@', 'a b@example.com'])('báo sai định dạng với email %j', (email) => {
    expect(getLookupEmailError(email)).toBe('Email không đúng định dạng.');
  });

  it('chấp nhận email hợp lệ có khoảng trắng hai đầu', () => {
    expect(getLookupEmailError('  khach@example.com ')).toBeNull();
  });
});
