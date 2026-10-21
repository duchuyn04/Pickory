import { describe, expect, it } from 'vitest';
import { rowNumber } from '../../../src/shared/row-number';

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
