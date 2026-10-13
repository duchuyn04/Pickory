import { describe, expect, it } from 'vitest';
import {
  VND_MAX_DIGITS,
  formatVndInput,
  parseVndInput,
  reformatVndInput,
} from '../../../src/shared/money-input-format';

describe('parseVndInput', () => {
  it.each([
    ['1250000', 1250000],
    ['1.250.000 đ', 1250000],
    ['1,250,000', 1250000],
    [' 1 250 000 ', 1250000],
  ])('đọc %j thành %d', (text, expected) => {
    expect(parseVndInput(text)).toBe(expected);
  });

  it.each([[''], ['abc'], ['đ'], ['   '], ['-'], [',']])(
    'trả null khi %j không còn chữ số',
    (text) => {
      expect(parseVndInput(text)).toBeNull();
    },
  );

  it('giữ số 0 và bỏ số 0 ở đầu', () => {
    expect(parseVndInput('0')).toBe(0);
    expect(parseVndInput('000')).toBe(0);
    expect(parseVndInput('007')).toBe(7);
  });

  it('bỏ dấu trừ và dấu thập phân như ký tự khác', () => {
    expect(parseVndInput('-500')).toBe(500);
    expect(parseVndInput('12,5')).toBe(125);
  });

  it('cắt phần chữ số vượt giới hạn để giá trị luôn là số nguyên an toàn', () => {
    const value = parseVndInput('9'.repeat(VND_MAX_DIGITS + 5));
    expect(value).toBe(Number('9'.repeat(VND_MAX_DIGITS)));
    expect(Number.isSafeInteger(value)).toBe(true);
  });
});

describe('formatVndInput', () => {
  it.each([
    [null, ''],
    [0, '0'],
    [7, '7'],
    [999, '999'],
    [1000, '1.000'],
    [1250000, '1.250.000'],
    [2147483647, '2.147.483.647'],
  ])('định dạng %j thành %j', (value, expected) => {
    expect(formatVndInput(value)).toBe(expected);
  });
});

describe('reformatVndInput', () => {
  it('gõ thêm một chữ số ở cuối đặt con trỏ ở cuối', () => {
    expect(reformatVndInput('1.2503', 6)).toEqual({ value: 12503, text: '12.503', caret: 6 });
  });

  it('chèn một chữ số giữa số giữ con trỏ sau chữ số vừa chèn', () => {
    // Trước khi gõ: `1.250.000`, con trỏ sau `1.2`. Gõ `9` cho `1.2950.000`.
    expect(reformatVndInput('1.2950.000', 4)).toEqual({
      value: 12950000,
      text: '12.950.000',
      caret: 4,
    });
  });

  it('xóa ngay sau dấu chấm giữ con trỏ trước dấu chấm, không nhảy về cuối', () => {
    // Trước khi xóa: `1.250.000`, con trỏ sau `1.`. Phím Backspace xóa dấu chấm.
    expect(reformatVndInput('1250.000', 1)).toEqual({
      value: 1250000,
      text: '1.250.000',
      caret: 1,
    });
  });

  it('xóa một chữ số giữa số làm các dấu chấm dồn lại nhưng con trỏ vẫn ở chỗ sửa', () => {
    // Trước khi xóa: `1.250.000`, con trỏ sau `1.25`. Phím Backspace xóa chữ số `5`.
    expect(reformatVndInput('1.20.000', 3)).toEqual({ value: 120000, text: '120.000', caret: 2 });
  });

  it('dán chuỗi có đơn vị và dấu chấm cho ra số đã định dạng, con trỏ ở cuối', () => {
    const pasted = '1.250.000 đ';
    expect(reformatVndInput(pasted, pasted.length)).toEqual({
      value: 1250000,
      text: '1.250.000',
      caret: 9,
    });
  });

  it('bỏ số 0 ở đầu và dịch con trỏ theo số chữ số còn lại', () => {
    expect(reformatVndInput('05', 2)).toEqual({ value: 5, text: '5', caret: 1 });
    expect(reformatVndInput('007', 3)).toEqual({ value: 7, text: '7', caret: 1 });
  });

  it('giữ một chữ số 0 khi toàn bộ chuỗi là số 0', () => {
    expect(reformatVndInput('000', 3)).toEqual({ value: 0, text: '0', caret: 1 });
  });

  it('xóa hết cho ra giá trị null và chuỗi rỗng', () => {
    expect(reformatVndInput('', 0)).toEqual({ value: null, text: '', caret: 0 });
  });

  it('ký tự không phải số trước con trỏ không tính vào vị trí', () => {
    expect(reformatVndInput('12a', 3)).toEqual({ value: 12, text: '12', caret: 2 });
  });

  it('trả null khi số chữ số vượt giới hạn để ô giữ nguyên giá trị cũ', () => {
    const tooLong = '1'.repeat(VND_MAX_DIGITS + 1);
    expect(reformatVndInput(tooLong, tooLong.length)).toBeNull();
  });

  it('chấp nhận đúng số chữ số tối đa', () => {
    const atLimit = '1'.repeat(VND_MAX_DIGITS);
    expect(reformatVndInput(atLimit, atLimit.length)?.value).toBe(Number(atLimit));
  });
});
