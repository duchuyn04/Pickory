export type RangeNumberKind = 'integer' | 'decimal';

export interface RangeInputErrors {
  minError: string | null;
  maxError: string | null;
}

const INTEGER_PATTERN = /^\d+$/;
const DECIMAL_PATTERN = /^-?\d+(\.\d+)?$/;

function validateBound(value: string, kind: RangeNumberKind): string | null {
  // Ô trống nghĩa là không giới hạn phía đó, nên không phải lỗi.
  if (value === '') return null;
  if (kind === 'integer') {
    return INTEGER_PATTERN.test(value) ? null : 'Nhập số nguyên không âm.';
  }
  return DECIMAL_PATTERN.test(value) ? null : 'Nhập một số.';
}

// Kiểm hai ô trước khi áp bộ lọc, vì API trả lỗi 400 cho số sai dạng hoặc khoảng ngược.
export function validateRangeInput(
  minValue: string,
  maxValue: string,
  kind: RangeNumberKind,
): RangeInputErrors {
  const minFormatError = validateBound(minValue, kind);
  const maxError = validateBound(maxValue, kind);
  if (minFormatError || maxError) return { minError: minFormatError, maxError };

  // Chỉ so sánh khi cả hai ô đều có số hợp lệ. So theo giá trị số vì "9" lớn hơn "10" khi so chữ.
  const bothFilled = minValue !== '' && maxValue !== '';
  if (bothFilled && Number(minValue) > Number(maxValue)) {
    return { minError: 'Giá trị "Từ" không được lớn hơn "Đến".', maxError: null };
  }
  return { minError: null, maxError: null };
}
