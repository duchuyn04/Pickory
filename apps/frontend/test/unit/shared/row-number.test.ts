import { describe, expect, it } from 'vitest';
import { rowNumber, treeRowNumber } from '../../../src/shared/row-number';

describe('rowNumber', () => {
  it('trang 1, limit 20, index 0 trả 1', () => {
    expect(rowNumber({ page: 1, limit: 20 }, 0)).toBe(1);
  });

  it('trang 2, limit 20, index 0 trả 21', () => {
    expect(rowNumber({ page: 2, limit: 20 }, 0)).toBe(21);
  });

  it('trang 3, limit 10, index 9 trả 30', () => {
    expect(rowNumber({ page: 3, limit: 10 }, 9)).toBe(30);
  });

  it('dòng cuối trang 1 và dòng đầu trang 2 liền số nhau', () => {
    expect(rowNumber({ page: 2, limit: 20 }, 0)).toBe(rowNumber({ page: 1, limit: 20 }, 19) + 1);
  });
});

describe('treeRowNumber', () => {
  it('gốc đầu tiên là 1', () => {
    expect(treeRowNumber(0)).toBe('1');
  });

  it('gốc thứ ba là 3', () => {
    expect(treeRowNumber(2)).toBe('3');
  });

  it('con đầu của gốc thứ hai là 2.1', () => {
    expect(treeRowNumber(1, 0)).toBe('2.1');
  });

  it('con thứ 10 của gốc đầu là 1.10, khác con đầu 1.1', () => {
    expect(treeRowNumber(0, 9)).toBe('1.10');
    expect(treeRowNumber(0, 9)).not.toBe(treeRowNumber(0, 0));
  });

  it('trả chuỗi, vì số thập phân làm mất số 0 cuối', () => {
    expect(typeof treeRowNumber(0, 9)).toBe('string');
  });
});
