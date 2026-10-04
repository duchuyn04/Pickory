import { describe, expect, it } from 'vitest';
import { getEmailError, getPasswordError } from '../../../../src/shared/identity-validation';

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
});
