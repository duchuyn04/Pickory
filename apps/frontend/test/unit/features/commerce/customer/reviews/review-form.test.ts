import { describe, expect, it } from 'vitest';
import {
  REVIEW_BODY_MAX_LENGTH,
  countReviewBodyCharacters,
  mapReviewServerErrors,
  toReviewPayload,
  validateReviewForm,
} from '../../../../../../src/features/commerce/customer/reviews/review-form';

describe('validateReviewForm', () => {
  it('thiếu số sao thì báo "Chọn số sao"', () => {
    expect(validateReviewForm({ rating: null, body: '' })).toEqual({ rating: 'Chọn số sao' });
  });

  it('nhận xét để trống là hợp lệ', () => {
    expect(validateReviewForm({ rating: 5, body: '' })).toEqual({});
  });

  it('nhận xét đúng 2.000 ký tự là hợp lệ', () => {
    const body = 'a'.repeat(REVIEW_BODY_MAX_LENGTH);

    expect(validateReviewForm({ rating: 3, body })).toEqual({});
  });

  it('nhận xét 2.001 ký tự bị từ chối', () => {
    const body = 'a'.repeat(REVIEW_BODY_MAX_LENGTH + 1);

    expect(validateReviewForm({ rating: 3, body }).body).toBe('Nhận xét tối đa 2.000 ký tự.');
  });

  it('khoảng trắng hai đầu không tính vào độ dài, khớp việc backend cắt khoảng trắng', () => {
    const body = ` ${'a'.repeat(REVIEW_BODY_MAX_LENGTH)} `;

    expect(validateReviewForm({ rating: 3, body })).toEqual({});
  });

  it('báo đủ hai lỗi cùng lúc', () => {
    const body = 'a'.repeat(REVIEW_BODY_MAX_LENGTH + 1);

    expect(Object.keys(validateReviewForm({ rating: null, body }))).toEqual(['rating', 'body']);
  });
});

describe('toReviewPayload', () => {
  it('cắt khoảng trắng và gửi null khi nhận xét trống', () => {
    expect(toReviewPayload({ rating: 4, body: '   ' })).toEqual({ rating: 4, body: null });
    expect(toReviewPayload({ rating: 4, body: ' Tốt \n' })).toEqual({ rating: 4, body: 'Tốt' });
  });
});

describe('mapReviewServerErrors', () => {
  it('gán lỗi 400 vào đúng trường rating và body', () => {
    const result = mapReviewServerErrors([
      { key: 'rating', message: 'Số sao phải là số nguyên từ 1 đến 5.' },
      { key: 'body', message: 'Nhận xét là chuỗi tối đa 2.000 ký tự.' },
    ]);

    expect(result.fieldErrors).toEqual({
      rating: 'Số sao phải là số nguyên từ 1 đến 5.',
      body: 'Nhận xét là chuỗi tối đa 2.000 ký tự.',
    });
    expect(result.unmatched).toEqual([]);
  });

  it('giữ lỗi không có trường tương ứng để hiện chung', () => {
    const result = mapReviewServerErrors([
      { key: 'extra', message: 'Trường này không được hỗ trợ.' },
    ]);

    expect(result.fieldErrors).toEqual({});
    expect(result.unmatched).toEqual(['Trường này không được hỗ trợ.']);
  });

  it('không có details thì không có lỗi nào', () => {
    expect(mapReviewServerErrors(undefined)).toEqual({ fieldErrors: {}, unmatched: [] });
  });
});

describe('countReviewBodyCharacters', () => {
  it('đếm sau khi cắt khoảng trắng hai đầu, cùng cách với luật kiểm và backend', () => {
    expect(countReviewBodyCharacters(' abc ')).toBe(3);
    expect(countReviewBodyCharacters('   ')).toBe(0);
  });

  it('2.000 ký tự cộng một khoảng trắng cuối vẫn không vượt giới hạn', () => {
    const body = `${'a'.repeat(REVIEW_BODY_MAX_LENGTH)} `;

    expect(countReviewBodyCharacters(body)).toBe(REVIEW_BODY_MAX_LENGTH);
    expect(validateReviewForm({ rating: 3, body })).toEqual({});
  });
});
