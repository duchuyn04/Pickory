import type { FieldErrors } from '../../../../shared/field-error';
import type { ReviewPayload } from './reviews-api';

// Nhận xét tối đa 2.000 ký tự. Khớp `ReviewRequests.BodyMaxLength` của backend.
export const REVIEW_BODY_MAX_LENGTH = 2000;

// Tên trường trùng với `key` mà backend trả trong `error.details`.
export type ReviewFieldName = 'rating' | 'body';

export type ReviewFieldErrors = FieldErrors<ReviewFieldName>;

export interface ReviewFormValues {
  // `null` khi khách chưa chọn sao.
  rating: number | null;
  body: string;
}

// Số ký tự của nhận xét theo cách backend đếm: cắt khoảng trắng hai đầu rồi mới đếm.
// Bộ đếm trên giao diện và luật kiểm cùng dùng hàm này, để hai nơi không lệch nhau.
export function countReviewBodyCharacters(body: string): number {
  return body.trim().length;
}

// Kiểm form với cùng luật của backend, để lỗi hiện ngay trước khi gửi request.
export function validateReviewForm(values: ReviewFormValues): ReviewFieldErrors {
  const errors: ReviewFieldErrors = {};

  if (values.rating === null) {
    errors.rating = 'Chọn số sao';
  }

  // Đếm bằng cùng hàm với bộ đếm trên giao diện, nên chữ đỏ và lỗi luôn xuất hiện cùng lúc.
  if (countReviewBodyCharacters(values.body) > REVIEW_BODY_MAX_LENGTH) {
    errors.body = 'Nhận xét tối đa 2.000 ký tự.';
  }

  return errors;
}

// Body gửi API. Nhận xét trống gửi `null`, vì backend lưu nhận xét toàn khoảng trắng thành `null`.
export function toReviewPayload(values: { rating: number; body: string }): ReviewPayload {
  const body = values.body.trim();
  return { rating: values.rating, body: body === '' ? null : body };
}

// Gán từng phần tử `details` của API vào trường cùng tên. Lỗi không có trường giữ lại để hiện chung.
export function mapReviewServerErrors(details: { key: string; message: string }[] | undefined): {
  fieldErrors: ReviewFieldErrors;
  unmatched: string[];
} {
  const fieldErrors: ReviewFieldErrors = {};
  const unmatched: string[] = [];
  for (const detail of details ?? []) {
    if (detail.key === 'rating' || detail.key === 'body') {
      // Giữ lỗi đầu tiên của mỗi trường, vì một ô chỉ hiện một câu lỗi.
      fieldErrors[detail.key] ??= detail.message;
    } else {
      unmatched.push(detail.message);
    }
  }
  return { fieldErrors, unmatched };
}
