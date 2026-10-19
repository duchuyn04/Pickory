import type { ApiError } from '../../shared/api';
import type { FieldErrors } from '../../shared/field-error';
import {
  FALLBACK_FEE_FIELDS,
  type FallbackFeeFieldName,
  type FallbackFeePayload,
  type FallbackFeePrices,
} from './fallback-fee-api';

export type FallbackFeeFieldErrors = FieldErrors<FallbackFeeFieldName>;

// Giá trị các ô ở dạng chuỗi, đúng như người dùng đang nhập.
export type FallbackFeeFormValues = Record<FallbackFeeFieldName, string>;

// Giới hạn khớp `UpdateFallbackFeeRequest` ở backend. Giá gốc và giá bước dùng chung một khoảng.
export const MAX_FEE = 5_000_000;
export const MIN_BASE_WEIGHT_GRAMS = 1;
export const MAX_BASE_WEIGHT_GRAMS = 20_000;

// Câu lỗi cùng chữ với backend, để lỗi từ client và lỗi từ server giống nhau dưới ô.
export const FEE_MESSAGE = 'Giá phải là số nguyên từ 0 đến 5.000.000 đồng.';
export const WEIGHT_MESSAGE = 'Khối lượng gốc phải là số nguyên từ 1 đến 20.000 gram.';
export const MISSING_MESSAGE = 'Cần nhập giá trị này khi bật phí dự phòng.';

// Chỉ chữ số ASCII. Dấu cộng, dấu trừ, dấu chấm và ký hiệu mũ không được ép thành số.
const INTEGER_PATTERN = /^\d+$/;

interface FieldRule {
  minimum: number;
  maximum: number;
  message: string;
}

// Ô khối lượng gốc có khoảng riêng. Sáu ô còn lại là tiền, dùng khoảng của giá.
export function ruleOf(field: FallbackFeeFieldName): FieldRule {
  if (field.endsWith('BaseWeightGrams')) {
    return {
      minimum: MIN_BASE_WEIGHT_GRAMS,
      maximum: MAX_BASE_WEIGHT_GRAMS,
      message: WEIGHT_MESSAGE,
    };
  }
  return { minimum: 0, maximum: MAX_FEE, message: FEE_MESSAGE };
}

// Trả số nguyên trong khoảng hoặc `null` khi chuỗi trống, không phải số nguyên không âm hoặc ngoài khoảng.
export function parseFieldValue(field: FallbackFeeFieldName, text: string): number | null {
  const trimmed = text.trim();
  if (!INTEGER_PATTERN.test(trimmed)) return null;
  const value = Number(trimmed);
  const { minimum, maximum } = ruleOf(field);
  if (!Number.isSafeInteger(value) || value < minimum || value > maximum) return null;
  return value;
}

export function emptyFormValues(): FallbackFeeFormValues {
  return Object.fromEntries(
    FALLBACK_FEE_FIELDS.map((field) => [field, '']),
  ) as FallbackFeeFormValues;
}

// Số `null` ở backend là chuỗi rỗng ở ô nhập, vì ô nhập chỉ giữ chuỗi.
export function toFormValues(prices: FallbackFeePrices): FallbackFeeFormValues {
  const values = emptyFormValues();
  for (const field of FALLBACK_FEE_FIELDS) {
    const price = prices[field];
    values[field] = price === null ? '' : String(price);
  }
  return values;
}

// Thân PUT gồm công tắc và chín giá, ô trống là `null`. Hàm chỉ chạy sau khi kiểm form, nên ô có chữ luôn là số hợp lệ.
export function toPayload(values: FallbackFeeFormValues, enabled: boolean): FallbackFeePayload {
  const prices = Object.fromEntries(
    FALLBACK_FEE_FIELDS.map((field) => {
      const trimmed = values[field].trim();
      return [field, trimmed === '' ? null : Number(trimmed)];
    }),
  ) as FallbackFeePrices;
  return { enabled, ...prices };
}

// Kiểm trước khi gửi để lỗi hiện dưới đúng ô mà không tốn một request. Backend vẫn kiểm lại toàn bộ.
export function validateFallbackFeeForm(
  values: FallbackFeeFormValues,
  enabled: boolean,
): FallbackFeeFieldErrors {
  const errors: FallbackFeeFieldErrors = {};

  for (const field of FALLBACK_FEE_FIELDS) {
    const text = values[field].trim();
    if (text === '') {
      // Tắt thì ô được trống. Bật thì cả chín ô phải có, vì thiếu một ô là một tuyến không tính được phí.
      if (enabled) errors[field] = MISSING_MESSAGE;
      continue;
    }
    // Ô đã nhập phải đúng khoảng dù đang tắt, giống backend.
    if (parseFieldValue(field, text) === null) errors[field] = ruleOf(field).message;
  }

  return errors;
}

export interface MappedServerErrors {
  fieldErrors: FallbackFeeFieldErrors;
  // Lỗi không gắn được vào ô nào của form. Màn hiện chúng bằng `ErrorAlert` đầu form.
  unmatched: string[];
}

function isFallbackFeeField(key: string): key is FallbackFeeFieldName {
  return (FALLBACK_FEE_FIELDS as readonly string[]).includes(key);
}

// Gán lỗi 400 `VALIDATION_ERROR` và lỗi 422 `FALLBACK_FEE_INCOMPLETE` vào đúng ô theo `details`.
// Khóa `enabled`, khóa lạ và lỗi cả body vào `unmatched`, vì không có ô nào để gắn.
export function mapServerErrors(error: ApiError): MappedServerErrors {
  const fieldErrors: FallbackFeeFieldErrors = {};
  const unmatched: string[] = [];

  // Lỗi không có `details` (500, 503, mạng) hiện nguyên câu `message` ở đầu form.
  const entries =
    error.details && error.details.length > 0
      ? error.details
      : [{ key: null as string | null, message: error.message }];

  for (const { key, message } of entries) {
    if (key !== null && isFallbackFeeField(key) && !fieldErrors[key]) {
      fieldErrors[key] = message;
    } else {
      unmatched.push(message);
    }
  }

  return { fieldErrors, unmatched };
}
