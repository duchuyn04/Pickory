import { describe, expect, it } from 'vitest';
import { getTabIndexForKey, wrapSlideIndex } from '../../../../src/features/home/carousel-index';

describe('wrapSlideIndex', () => {
  it('quay về banner đầu sau banner cuối, và ngược lại', () => {
    expect(wrapSlideIndex(3, 3)).toBe(0);
    expect(wrapSlideIndex(-1, 3)).toBe(2);
    expect(wrapSlideIndex(1, 3)).toBe(1);
  });

  it('trả 0 khi không có banner nào', () => {
    expect(wrapSlideIndex(2, 0)).toBe(0);
  });
});

describe('getTabIndexForKey', () => {
  it('đi qua các tab bằng phím mũi tên, Home và End', () => {
    expect(getTabIndexForKey('ArrowRight', 2, 3)).toBe(0);
    expect(getTabIndexForKey('ArrowLeft', 0, 3)).toBe(2);
    expect(getTabIndexForKey('Home', 2, 3)).toBe(0);
    expect(getTabIndexForKey('End', 0, 3)).toBe(2);
  });

  it('bỏ qua phím khác', () => {
    expect(getTabIndexForKey('Enter', 1, 3)).toBeNull();
  });
});
