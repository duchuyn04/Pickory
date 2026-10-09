import { describe, expect, it } from 'vitest';
import { contrastRatio } from './color-contrast';

describe('contrastRatio', () => {
  it('đen trên trắng đạt tỉ lệ tối đa 21', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 5);
  });

  it('hai màu giống nhau có tỉ lệ 1', () => {
    expect(contrastRatio('#ffffff', '#ffffff')).toBeCloseTo(1, 5);
  });

  it('mã 3 ký tự cho cùng kết quả với mã 6 ký tự', () => {
    expect(contrastRatio('#fff', '#000')).toBeCloseTo(contrastRatio('#ffffff', '#000000'), 5);
  });

  it('kết quả không phụ thuộc thứ tự màu', () => {
    expect(contrastRatio('#d70018', '#ffffff')).toBeCloseTo(contrastRatio('#ffffff', '#d70018'), 5);
  });

  it('khớp giá trị tham chiếu của màu xám #767676 trên trắng', () => {
    expect(contrastRatio('#767676', '#ffffff')).toBeCloseTo(4.54, 2);
  });

  it('từ chối mã màu không phải hex', () => {
    expect(() => contrastRatio('red', '#ffffff')).toThrow('Mã màu không hợp lệ');
  });
});
