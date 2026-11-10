import { ApiError } from '../../../shared/api';
import type { FieldErrors } from '../../../shared/field-error';

export interface MappedFieldErrors<Field extends string> {
  fieldErrors: FieldErrors<Field>;
  // Lỗi không gắn được vào ô nào. Màn hiện chúng bằng `ErrorAlert`.
  unmatched: string[];
}

// Gắn lỗi `VALIDATION_ERROR` vào đúng ô theo `details[].key`. Khóa lạ và lỗi không có `details` vào `unmatched`.
export function mapValidationErrors<Field extends string>(
  error: ApiError,
  keyToField: Record<string, Field>,
): MappedFieldErrors<Field> {
  const fieldErrors: FieldErrors<Field> = {};
  const unmatched: string[] = [];
  // Lỗi không có mã `VALIDATION_ERROR` hoặc thiếu `details` không có khóa để gắn, nên hiện nguyên câu của API.
  if (error.code !== 'VALIDATION_ERROR' || !error.details || error.details.length === 0) {
    return { fieldErrors, unmatched: [error.message] };
  }
  for (const detail of error.details) {
    const field = keyToField[detail.key];
    // Mỗi ô chỉ giữ lỗi đầu tiên, để một ô không hiện hai câu chồng nhau.
    if (field && !fieldErrors[field]) {
      fieldErrors[field] = detail.message;
    } else {
      unmatched.push(detail.message);
    }
  }
  return { fieldErrors, unmatched };
}
