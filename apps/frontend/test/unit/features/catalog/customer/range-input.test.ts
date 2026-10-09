import { describe, expect, it } from 'vitest';
import { validateRangeInput } from '../../../../../src/features/catalog/customer/range-input';

describe('kiểm khoảng "Từ" và "Đến"', () => {
  it('hai ô trống hợp lệ vì nghĩa là bỏ lọc', () => {
    expect(validateRangeInput('', '', 'integer')).toEqual({ minError: null, maxError: null });
  });

  it('khoảng đúng thứ tự hợp lệ, kể cả hai cận bằng nhau', () => {
    expect(validateRangeInput('1', '5', 'integer')).toEqual({ minError: null, maxError: null });
    expect(validateRangeInput('5', '5', 'integer')).toEqual({ minError: null, maxError: null });
  });

  it('giá chỉ nhận số nguyên không âm', () => {
    expect(validateRangeInput('1.5', '-3', 'integer')).toEqual({
      minError: 'Nhập số nguyên không âm.',
      maxError: 'Nhập số nguyên không âm.',
    });
  });

  it('thuộc tính số nhận số thập phân và số âm', () => {
    expect(validateRangeInput('-1.5', '15.6', 'decimal')).toEqual({
      minError: null,
      maxError: null,
    });
  });

  it('thuộc tính số từ chối chữ và ký hiệu mũ', () => {
    expect(validateRangeInput('abc', '1e3', 'decimal')).toEqual({
      minError: 'Nhập một số.',
      maxError: 'Nhập một số.',
    });
  });

  it('cận dưới lớn hơn cận trên báo lỗi ở ô "Từ"', () => {
    expect(validateRangeInput('10', '2', 'integer')).toEqual({
      minError: 'Giá trị "Từ" không được lớn hơn "Đến".',
      maxError: null,
    });
  });

  it('so sánh theo giá trị số, không theo thứ tự chữ', () => {
    expect(validateRangeInput('9', '10', 'integer')).toEqual({ minError: null, maxError: null });
  });
});
