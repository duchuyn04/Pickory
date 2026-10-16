import { describe, expect, it } from 'vitest';
import type { ReviewSummary } from '../../../../../../src/features/commerce/customer/reviews/reviews-api';
import {
  buildDistributionRows,
  formatAverageRating,
} from '../../../../../../src/features/commerce/customer/reviews/review-summary';

function buildSummary(distribution: Record<string, number>): ReviewSummary {
  const count = Object.values(distribution).reduce((total, value) => total + value, 0);
  return { average: null, count, distribution };
}

describe('formatAverageRating', () => {
  it('hiện một chữ số thập phân theo kiểu Việt Nam', () => {
    expect(formatAverageRating(4.5)).toBe('4,5');
    expect(formatAverageRating(4)).toBe('4,0');
    expect(formatAverageRating(3.666)).toBe('3,7');
  });

  it('trả null khi chưa có đánh giá, để giao diện hiện trạng thái rỗng thay cho NaN hay 0,0', () => {
    expect(formatAverageRating(null)).toBeNull();
  });
});

describe('buildDistributionRows', () => {
  it('xếp năm dòng từ 5 sao xuống 1 sao, kèm số lượng và tỉ lệ phần trăm', () => {
    const rows = buildDistributionRows(buildSummary({ '1': 0, '2': 1, '3': 1, '4': 2, '5': 4 }));

    expect(rows.map((row) => row.stars)).toEqual([5, 4, 3, 2, 1]);
    expect(rows.map((row) => row.count)).toEqual([4, 2, 1, 1, 0]);
    expect(rows.map((row) => row.percent)).toEqual([50, 25, 12.5, 12.5, 0]);
  });

  it('có tên truy cập nói rõ số sao và số đánh giá', () => {
    const rows = buildDistributionRows(buildSummary({ '1': 0, '2': 0, '3': 0, '4': 0, '5': 3 }));

    expect(rows[0]?.label).toBe('5 sao: 3 đánh giá');
    expect(rows[4]?.label).toBe('1 sao: 0 đánh giá');
  });

  it('trả tỉ lệ 0 cho mọi dòng khi count bằng 0, không chia cho 0', () => {
    const rows = buildDistributionRows(buildSummary({ '1': 0, '2': 0, '3': 0, '4': 0, '5': 0 }));

    expect(rows.map((row) => row.percent)).toEqual([0, 0, 0, 0, 0]);
  });

  it('coi khóa thiếu là 0 đánh giá', () => {
    const rows = buildDistributionRows({ average: 5, count: 1, distribution: { '5': 1 } });

    expect(rows.map((row) => row.count)).toEqual([1, 0, 0, 0, 0]);
  });
});
