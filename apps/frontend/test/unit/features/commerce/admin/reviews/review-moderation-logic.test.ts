import { describe, expect, it } from 'vitest';
import {
  HIDE_REASON_CODES,
  PRIORITY_REPORT_THRESHOLD,
  SHOW_REASON_CODES,
  formatReasonLabel,
  isPriorityReview,
  reasonCodesFor,
  validateReasonChoice,
} from '../../../../../../src/features/commerce/admin/reviews/review-moderation-logic';
import {
  buildAdminReviewsPath,
  normalizeStatusTab,
} from '../../../../../../src/features/commerce/admin/reviews/admin-reviews-api';

describe('formatReasonLabel', () => {
  it.each([
    ['spam', 'Spam hoặc quảng cáo'],
    ['offensive', 'Ngôn từ xúc phạm'],
    ['personal_info', 'Chứa thông tin cá nhân'],
    ['off_topic', 'Không liên quan đến sản phẩm'],
    ['misleading', 'Sai sự thật, gây hiểu lầm'],
    ['report_rejected', 'Báo cáo không đúng'],
    ['hidden_by_mistake', 'Ẩn nhầm'],
  ])('mã %s có nhãn tiếng Việt', (code, label) => {
    expect(formatReasonLabel(code)).toBe(label);
  });

  it('mã lạ hiện nguyên mã, không làm vỡ trang', () => {
    expect(formatReasonLabel('new_reason')).toBe('new_reason');
  });

  it('không có mã thì trả chuỗi rỗng', () => {
    expect(formatReasonLabel(null)).toBe('');
  });
});

describe('reasonCodesFor', () => {
  it('ẩn có đúng 5 mã, khớp danh sách của backend', () => {
    expect(reasonCodesFor('hide')).toEqual([
      'spam',
      'offensive',
      'personal_info',
      'off_topic',
      'misleading',
    ]);
    expect(reasonCodesFor('hide')).toBe(HIDE_REASON_CODES);
  });

  it('hiện lại có đúng 2 mã, khớp danh sách của backend', () => {
    expect(reasonCodesFor('show')).toEqual(['report_rejected', 'hidden_by_mistake']);
    expect(reasonCodesFor('show')).toBe(SHOW_REASON_CODES);
  });

  it('mọi mã đều có nhãn riêng, khác chính mã đó', () => {
    for (const code of [...HIDE_REASON_CODES, ...SHOW_REASON_CODES]) {
      expect(formatReasonLabel(code)).not.toBe(code);
    }
  });
});

describe('isPriorityReview', () => {
  it('ngưỡng là 3 báo cáo, khớp PriorityReportThreshold của backend', () => {
    expect(PRIORITY_REPORT_THRESHOLD).toBe(3);
  });

  it('2 báo cáo thì không ưu tiên, 3 báo cáo thì ưu tiên', () => {
    expect(isPriorityReview(2)).toBe(false);
    expect(isPriorityReview(3)).toBe(true);
    expect(isPriorityReview(10)).toBe(true);
    expect(isPriorityReview(0)).toBe(false);
  });
});

describe('validateReasonChoice', () => {
  it('chưa chọn lý do thì báo "Chọn lý do"', () => {
    expect(validateReasonChoice('hide', '')).toBe('Chọn lý do');
    expect(validateReasonChoice('show', '')).toBe('Chọn lý do');
  });

  it('lý do của hành động kia bị từ chối', () => {
    expect(validateReasonChoice('show', 'spam')).toBe('Chọn lý do');
    expect(validateReasonChoice('hide', 'report_rejected')).toBe('Chọn lý do');
  });

  it('lý do đúng hành động thì hợp lệ', () => {
    expect(validateReasonChoice('hide', 'spam')).toBeNull();
    expect(validateReasonChoice('show', 'hidden_by_mistake')).toBeNull();
  });
});

describe('normalizeStatusTab', () => {
  it('chỉ nhận visible và hidden, giá trị khác về tab Tất cả', () => {
    expect(normalizeStatusTab('visible')).toBe('visible');
    expect(normalizeStatusTab('hidden')).toBe('hidden');
    expect(normalizeStatusTab('')).toBe('');
    expect(normalizeStatusTab('archived')).toBe('');
    expect(normalizeStatusTab('VISIBLE')).toBe('');
  });
});

describe('buildAdminReviewsPath', () => {
  it('không có bộ lọc và ở trang 1 thì không gửi tham số', () => {
    expect(buildAdminReviewsPath('', 1)).toBe('/api/admin/reviews');
  });

  it('gửi status và page khi có', () => {
    expect(buildAdminReviewsPath('hidden', 2)).toBe('/api/admin/reviews?status=hidden&page=2');
    expect(buildAdminReviewsPath('', 3)).toBe('/api/admin/reviews?page=3');
    expect(buildAdminReviewsPath('visible', 1)).toBe('/api/admin/reviews?status=visible');
  });
});
