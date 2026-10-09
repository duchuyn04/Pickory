import { describe, expect, it } from 'vitest';
import {
  getConfirmPasswordError,
  getEmailError,
  getNameError,
  getPasswordError,
  getPhoneError,
} from '../../../src/shared/identity-validation';

describe('validation form identity', () => {
  it.each(['', 'abc@'])('từ chối email không hợp lệ %j trước khi gửi form', (email) => {
    expect(getEmailError(email)).not.toBeNull();
  });

  it('chấp nhận email hợp lệ không phân biệt chữ hoa', () => {
    expect(getEmailError('MT-customer@Pickory.Test')).toBeNull();
  });

  it('áp dụng biên 8 đến 128 ký tự cho mật khẩu mới', () => {
    expect(getPasswordError('a'.repeat(7))).not.toBeNull();
    expect(getPasswordError('a'.repeat(8))).toBeNull();
    expect(getPasswordError('a'.repeat(128))).toBeNull();
    expect(getPasswordError('a'.repeat(129))).not.toBeNull();
  });

  it('không áp giới hạn tối thiểu của mật khẩu mới lên mật khẩu đăng nhập', () => {
    expect(getPasswordError('a', 1)).toBeNull();
    expect(getPasswordError('', 1)).not.toBeNull();
    expect(getPasswordError('a'.repeat(129), 1)).not.toBeNull();
  });

  it('ô xác nhận trống báo phải nhập lại mật khẩu', () => {
    expect(getConfirmPasswordError('matkhaumoi123', '')).toBe('Vui lòng xác nhận lại mật khẩu.');
  });

  it('ô xác nhận khác mật khẩu mới báo không khớp', () => {
    expect(getConfirmPasswordError('matkhaumoi123', 'matkhaukhac123')).toBe(
      'Mật khẩu xác nhận không khớp.',
    );
  });

  it('ô xác nhận trùng mật khẩu mới thì hợp lệ', () => {
    expect(getConfirmPasswordError('matkhaumoi123', 'matkhaumoi123')).toBeNull();
  });

  it.each(['', '   ', 'Nguyễn Văn A', '  Nguyễn Văn A  '])('chấp nhận tên %j', (name) => {
    expect(getNameError(name)).toBeNull();
  });

  it('chấp nhận tên đúng 100 ký tự có dấu và từ chối tên 101 ký tự', () => {
    expect(getNameError('Ệ'.repeat(100))).toBeNull();
    expect(getNameError('Ệ'.repeat(101))).toBe('Họ và tên tối đa 100 ký tự.');
  });

  it('đếm chữ có dấu viết dạng tổ hợp là một ký tự', () => {
    expect(getNameError('E\u0323\u0302'.repeat(100))).toBeNull();
  });

  it('không tính khoảng trắng hai đầu vào độ dài tên', () => {
    expect(getNameError(`  ${'a'.repeat(100)}  `)).toBeNull();
  });

  it.each(['Tên\u0000x', 'Tên\nxuống dòng', 'Tên\ttab', 'Tên\u007Fx'])(
    'từ chối tên có ký tự điều khiển %j',
    (name) => {
      expect(getNameError(name)).toBe('Họ và tên chứa ký tự không hợp lệ.');
    },
  );

  it.each([
    '',
    '   ',
    '0912345678',
    '0912 345 678',
    '091.234.5678',
    '091-234-5678',
    '+84912345678',
  ])('chấp nhận số điện thoại %j', (phone) => {
    expect(getPhoneError(phone)).toBeNull();
  });

  it.each([
    'abc',
    '12345',
    '0123456789',
    '+8412345678',
    '0212345678',
    '091234567',
    '09123456789',
    '+840912345678',
    '84912345678',
    '0912a45678',
    '٠٩١٢٣٤٥٦٧٨',
  ])('từ chối số điện thoại %j', (phone) => {
    expect(getPhoneError(phone)).toBe('Số điện thoại không đúng định dạng.');
  });
});
