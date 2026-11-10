import { describe, expect, it } from 'vitest';
import {
  addHoliday,
  DUPLICATE_DAY_ERROR,
  EMPTY_DAY_ERROR,
  formatIsoDay,
  INVALID_DAY_ERROR,
  isValidIsoDay,
  removeHoliday,
  sortHolidays,
} from '../../../../../src/features/support-desk/admin/holiday-dates';

describe('addHoliday', () => {
  it('thêm ngày mới và giữ danh sách tăng dần', () => {
    const result = addHoliday(['2027-02-07', '2027-12-25'], '2027-02-06');

    expect(result).toEqual({ ok: true, holidays: ['2027-02-06', '2027-02-07', '2027-12-25'] });
  });

  it('không thêm ngày đã có và trả lỗi trùng', () => {
    const result = addHoliday(['2027-02-06'], '2027-02-06');

    expect(result).toEqual({ ok: false, error: DUPLICATE_DAY_ERROR });
    expect(DUPLICATE_DAY_ERROR).toBe('Ngày này đã có trong danh sách.');
  });

  it('ô ngày trống trả lỗi chọn ngày', () => {
    expect(addHoliday([], '')).toEqual({ ok: false, error: EMPTY_DAY_ERROR });
    expect(EMPTY_DAY_ERROR).toBe('Hãy chọn một ngày.');
  });

  it('ngày không có thật trả lỗi không hợp lệ', () => {
    expect(addHoliday([], '2027-02-30')).toEqual({ ok: false, error: INVALID_DAY_ERROR });
    expect(addHoliday([], '20270-02-06')).toEqual({ ok: false, error: INVALID_DAY_ERROR });
  });

  it('không sửa mảng đầu vào', () => {
    const current = ['2027-02-07'];

    addHoliday(current, '2027-02-06');

    expect(current).toEqual(['2027-02-07']);
  });
});

describe('removeHoliday', () => {
  it('chỉ xóa đúng ngày được chọn', () => {
    expect(removeHoliday(['2027-02-06', '2027-02-07'], '2027-02-06')).toEqual(['2027-02-07']);
  });
});

describe('sortHolidays', () => {
  it('xếp tăng dần theo thời gian, không theo thứ tự thêm', () => {
    expect(sortHolidays(['2028-01-01', '2027-12-31', '2027-02-06'])).toEqual([
      '2027-02-06',
      '2027-12-31',
      '2028-01-01',
    ]);
  });
});

describe('formatIsoDay', () => {
  it('đổi yyyy-MM-dd thành dd/MM/yyyy mà không qua múi giờ', () => {
    expect(formatIsoDay('2027-02-06')).toBe('06/02/2027');
    expect(formatIsoDay('2027-01-01')).toBe('01/01/2027');
  });
});

describe('isValidIsoDay', () => {
  it('nhận năm nhuận và loại ngày 29/02 của năm thường', () => {
    expect(isValidIsoDay('2028-02-29')).toBe(true);
    expect(isValidIsoDay('2027-02-29')).toBe(false);
    expect(isValidIsoDay('2100-02-29')).toBe(false);
    expect(isValidIsoDay('2000-02-29')).toBe(true);
  });

  it('loại tháng, ngày ngoài khoảng và chuỗi sai dạng', () => {
    expect(isValidIsoDay('2027-13-01')).toBe(false);
    expect(isValidIsoDay('2027-04-31')).toBe(false);
    expect(isValidIsoDay('2027-00-10')).toBe(false);
    expect(isValidIsoDay('2027-1-1')).toBe(false);
  });
});
