import { describe, expect, it } from 'vitest';
import { myReviewStatusSchema } from '../../../../../../src/features/commerce/customer/reviews/review-schemas';
import { findCommerceExample, listCommerceExamples } from '../../contract-fixtures';
import { exampleData } from '../../fixture-response';

const examples = listCommerceExamples('review-eligibility.json');

// Đánh giá đầy đủ do test dựng, vì fixture chỉ có `review: null`.
function fullReview(): Record<string, unknown> {
  return {
    id: 'r-1',
    rating: 5,
    body: 'Dùng tốt',
    hidden: false,
    createdAt: '2026-11-19T03:00:00Z',
    updatedAt: '2026-11-19T03:00:00Z',
    editableUntil: '2026-11-20T03:00:00Z',
  };
}

// Trạng thái hợp lệ để mỗi case sửa một khóa.
function status(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return { eligible: true, reason: null, review: null, ...overrides };
}

function firstIssuePath(value: unknown): PropertyKey[] | undefined {
  return myReviewStatusSchema.safeParse(value).error?.issues[0]?.path;
}

describe('schema response GET /reviews/mine', () => {
  it('có đúng hai ví dụ để test không đỏ giả vì danh sách rỗng', () => {
    expect(examples).toHaveLength(2);
  });

  it.each(['order shipping', 'order delivered'])('đọc được ví dụ "%s"', (name) => {
    const example = findCommerceExample('review-eligibility.json', name);
    const data = exampleData(example);

    expect(myReviewStatusSchema.safeParse(data).success).toBe(true);
  });

  it('giữ nguyên giá trị của ví dụ order shipping', () => {
    const example = findCommerceExample('review-eligibility.json', 'order shipping');
    const data = exampleData(example);

    expect(myReviewStatusSchema.parse(data)).toEqual({
      eligible: false,
      reason: 'NOT_PURCHASED',
      review: null,
    });
  });

  it('báo lỗi khi reason là mã lạ', () => {
    expect(firstIssuePath(status({ reason: 'KHAC' }))).toEqual(['reason']);
  });

  it('chỉ nhận đúng hai mã lý do của backend', () => {
    // Danh sách mã là hợp đồng với backend. Thêm mã thứ ba vào schema phải làm test này đỏ.
    expect(myReviewStatusSchema.shape.reason.unwrap().options).toEqual([
      'NOT_PURCHASED',
      'ALREADY_REVIEWED',
    ]);
  });

  it('báo lỗi khi thiếu eligible', () => {
    const value = status();
    delete value.eligible;

    expect(firstIssuePath(value)).toEqual(['eligible']);
  });

  it('báo lỗi khi thiếu review', () => {
    const value = status();
    delete value.review;

    expect(firstIssuePath(value)).toEqual(['review']);
  });

  it('báo lỗi khi eligible là chuỗi', () => {
    expect(firstIssuePath(status({ eligible: 'true' }))).toEqual(['eligible']);
  });

  it('đọc được review đầy đủ', () => {
    expect(myReviewStatusSchema.safeParse(status({ review: fullReview() })).success).toBe(true);
  });

  it('báo lỗi khi rating là 6', () => {
    const review = { ...fullReview(), rating: 6 };

    expect(firstIssuePath(status({ review }))).toEqual(['review', 'rating']);
  });

  it('báo lỗi khi hidden là chuỗi', () => {
    const review = { ...fullReview(), hidden: 'false' };

    expect(firstIssuePath(status({ review }))).toEqual(['review', 'hidden']);
  });
});
