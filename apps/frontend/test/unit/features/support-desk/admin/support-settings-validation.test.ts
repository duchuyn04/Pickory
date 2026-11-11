import { describe, expect, it } from 'vitest';
import {
  parseMinutes,
  validateSupportSettings,
  type SupportSettingsFormValues,
} from '../../../../../src/features/support-desk/admin/support-settings-validation';

const VALID_VALUES: SupportSettingsFormValues = {
  opensAt: '08:00',
  closesAt: '21:00',
  waitNudgeMinutes: '10',
  autoCloseMinutes: '30',
};

function validate(overrides: Partial<SupportSettingsFormValues>) {
  return validateSupportSettings({ ...VALID_VALUES, ...overrides });
}

describe('validateSupportSettings', () => {
  it('giá trị của ví dụ mẫu không có lỗi', () => {
    expect(validate({})).toEqual({});
  });

  it('giờ mở cửa sau giờ đóng cửa là lỗi dưới ô giờ mở cửa', () => {
    expect(validate({ opensAt: '22:00', closesAt: '21:00' })).toEqual({
      opensAt: 'Giờ mở cửa phải trước giờ đóng cửa.',
    });
  });

  it('giờ mở cửa bằng giờ đóng cửa cũng là lỗi', () => {
    expect(validate({ opensAt: '21:00', closesAt: '21:00' }).opensAt).toBe(
      'Giờ mở cửa phải trước giờ đóng cửa.',
    );
  });

  it('ô giờ trống báo lỗi riêng cho từng ô', () => {
    expect(validate({ opensAt: '', closesAt: '' })).toEqual({
      opensAt: 'Hãy chọn giờ mở cửa.',
      closesAt: 'Hãy chọn giờ đóng cửa.',
    });
  });

  it('mốc nhắc lại bằng mốc tự đóng là lỗi dưới ô mốc nhắc lại', () => {
    expect(validate({ waitNudgeMinutes: '30', autoCloseMinutes: '30' })).toEqual({
      waitNudgeMinutes: 'Mốc nhắc lại phải nhỏ hơn mốc tự đóng.',
    });
  });

  it('mốc nhắc lại lớn hơn mốc tự đóng là lỗi', () => {
    expect(validate({ waitNudgeMinutes: '31', autoCloseMinutes: '30' }).waitNudgeMinutes).toBe(
      'Mốc nhắc lại phải nhỏ hơn mốc tự đóng.',
    );
  });

  it('mốc nhắc lại nhỏ hơn mốc tự đóng 1 phút là hợp lệ', () => {
    expect(validate({ waitNudgeMinutes: '29', autoCloseMinutes: '30' })).toEqual({});
  });

  it.each(['', '0', '241', '1.5', '-3', 'abc', ' '])(
    'mốc phút %j ngoài 1 đến 240 hoặc không phải số nguyên là lỗi ở đúng ô',
    (badValue) => {
      const errors = validate({ autoCloseMinutes: badValue });

      expect(errors.autoCloseMinutes).toBe('Nhập số nguyên từ 1 đến 240.');
      expect(errors.waitNudgeMinutes).toBeUndefined();
    },
  );

  it('chấp nhận hai đầu khoảng 1 và 240', () => {
    expect(validate({ waitNudgeMinutes: '1', autoCloseMinutes: '240' })).toEqual({});
  });

  it('mốc nhắc lại 240 là lỗi vì không nhỏ hơn mốc tự đóng tối đa', () => {
    expect(validate({ waitNudgeMinutes: '240', autoCloseMinutes: '240' }).waitNudgeMinutes).toBe(
      'Mốc nhắc lại phải nhỏ hơn mốc tự đóng.',
    );
  });
});

describe('parseMinutes', () => {
  it('đọc số nguyên có khoảng trắng hai đầu', () => {
    expect(parseMinutes(' 15 ')).toBe(15);
  });

  it('trả null cho số lẻ, số thập phân kiểu e và ô trống', () => {
    expect(parseMinutes('1e2')).toBeNull();
    expect(parseMinutes('10.0')).toBeNull();
    expect(parseMinutes('')).toBeNull();
  });
});
