import type { ApiError } from '../../shared/api';
import type { FieldErrors } from '../../shared/field-error';

export type CommerceFieldName =
  | 'freeShippingThreshold'
  | 'codMaxAmount'
  | 'maxQuantityPerSku'
  | 'maxCartLines'
  | 'sepayAccountNumber'
  | 'sepayBankCode';

export type CommerceFieldErrors = FieldErrors<CommerceFieldName>;

// Giá trị các ô ở dạng chuỗi, đúng như người dùng đang nhập.
export type CommerceFormValues = Record<CommerceFieldName, string>;

// Giới hạn khớp luật kiểm của `UpdateCommerceSettingsRequest` ở backend.
export const MONEY_MAX_AMOUNT = 1_000_000_000;
export const MAX_QUANTITY_PER_SKU_UPPER_BOUND = 99;
export const MAX_CART_LINES_UPPER_BOUND = 100;
const ACCOUNT_NUMBER_MIN_LENGTH = 6;
const ACCOUNT_NUMBER_MAX_LENGTH = 20;
const BANK_CODE_MIN_LENGTH = 2;
const BANK_CODE_MAX_LENGTH = 20;

const PAIRED_FIELD_MESSAGE = 'Cần nhập cả số tài khoản và mã ngân hàng, hoặc để trống cả hai.';

// Chỉ chữ số ASCII. Dấu cộng, dấu trừ, dấu chấm và ký hiệu mũ không được ép thành số.
const INTEGER_PATTERN = /^\d+$/;
const ACCOUNT_NUMBER_PATTERN = /^[0-9]+$/;
const BANK_CODE_PATTERN = /^[0-9A-Za-z]+$/;

// Số nguyên dương có định dạng VND, ví dụ `500.000 đ`. Dùng cho gợi ý dưới ô tiền.
export function formatVnd(amount: number): string {
  return `${amount.toLocaleString('vi-VN')} đ`;
}

export function formatCount(value: number): string {
  return value.toLocaleString('vi-VN');
}

interface NumberRule {
  field: CommerceFieldName;
  minimum: number;
  maximum: number;
  message: string;
}

const NUMBER_RULES: NumberRule[] = [
  {
    field: 'freeShippingThreshold',
    minimum: 0,
    maximum: MONEY_MAX_AMOUNT,
    message: `Ngưỡng miễn phí giao hàng phải là số nguyên từ 0 đến ${formatCount(MONEY_MAX_AMOUNT)} đ.`,
  },
  {
    field: 'codMaxAmount',
    minimum: 0,
    maximum: MONEY_MAX_AMOUNT,
    message: `Hạn mức COD phải là số nguyên từ 0 đến ${formatCount(MONEY_MAX_AMOUNT)} đ.`,
  },
  {
    field: 'maxQuantityPerSku',
    minimum: 1,
    maximum: MAX_QUANTITY_PER_SKU_UPPER_BOUND,
    message: `Số lượng tối đa mỗi sản phẩm phải là số nguyên từ 1 đến ${MAX_QUANTITY_PER_SKU_UPPER_BOUND}.`,
  },
  {
    field: 'maxCartLines',
    minimum: 1,
    maximum: MAX_CART_LINES_UPPER_BOUND,
    message: `Số dòng tối đa mỗi giỏ phải là số nguyên từ 1 đến ${MAX_CART_LINES_UPPER_BOUND}.`,
  },
];

// Trả số nguyên hợp lệ hoặc `null` khi chuỗi không phải số nguyên không âm trong khoảng.
export function parseBoundedInteger(text: string, minimum: number, maximum: number): number | null {
  const trimmed = text.trim();
  if (!INTEGER_PATTERN.test(trimmed)) return null;
  const value = Number(trimmed);
  if (!Number.isSafeInteger(value) || value < minimum || value > maximum) return null;
  return value;
}

// Kiểm trước khi gửi để lỗi hiện dưới đúng ô mà không tốn một request. Backend vẫn kiểm lại toàn bộ.
export function validateCommerceForm(values: CommerceFormValues): CommerceFieldErrors {
  const errors: CommerceFieldErrors = {};

  for (const rule of NUMBER_RULES) {
    if (parseBoundedInteger(values[rule.field], rule.minimum, rule.maximum) === null) {
      errors[rule.field] = rule.message;
    }
  }

  const accountNumber = values.sepayAccountNumber.trim();
  const bankCode = values.sepayBankCode.trim();

  const accountNumberIsValid =
    ACCOUNT_NUMBER_PATTERN.test(accountNumber) &&
    accountNumber.length >= ACCOUNT_NUMBER_MIN_LENGTH &&
    accountNumber.length <= ACCOUNT_NUMBER_MAX_LENGTH;
  if (accountNumber && !accountNumberIsValid) {
    errors.sepayAccountNumber = `Số tài khoản gồm từ ${ACCOUNT_NUMBER_MIN_LENGTH} đến ${ACCOUNT_NUMBER_MAX_LENGTH} chữ số.`;
  }

  const bankCodeIsValid =
    BANK_CODE_PATTERN.test(bankCode) &&
    bankCode.length >= BANK_CODE_MIN_LENGTH &&
    bankCode.length <= BANK_CODE_MAX_LENGTH;
  if (bankCode && !bankCodeIsValid) {
    errors.sepayBankCode = `Mã ngân hàng gồm từ ${BANK_CODE_MIN_LENGTH} đến ${BANK_CODE_MAX_LENGTH} chữ cái hoặc chữ số.`;
  }

  // Hai ô đi cùng nhau. Lỗi đặt ở ô còn trống để người dùng biết cần điền ô nào.
  if (accountNumber && !bankCode) errors.sepayBankCode = PAIRED_FIELD_MESSAGE;
  if (bankCode && !accountNumber) errors.sepayAccountNumber = PAIRED_FIELD_MESSAGE;

  return errors;
}

export interface MappedServerErrors {
  fieldErrors: CommerceFieldErrors;
  // Lỗi không gắn được vào ô nào của form. Màn hiện chúng bằng `ErrorAlert` đầu form.
  unmatched: string[];
}

// Khóa của backend (tên trường JSON) cũng là tên ô trong form.
const FORM_FIELDS: readonly string[] = [
  'freeShippingThreshold',
  'codMaxAmount',
  'maxQuantityPerSku',
  'maxCartLines',
  'sepayAccountNumber',
  'sepayBankCode',
];

function isFormField(key: string): key is CommerceFieldName {
  return FORM_FIELDS.includes(key);
}

// Gán lỗi của backend vào đúng ô theo `details`. Khóa lạ và lỗi cả body vào `unmatched`.
export function mapServerErrors(error: ApiError): MappedServerErrors {
  const fieldErrors: CommerceFieldErrors = {};
  const unmatched: string[] = [];

  // Lỗi không có `details` (500, 503, mạng) hiện nguyên câu `message` ở đầu form.
  const entries =
    error.details && error.details.length > 0
      ? error.details
      : [{ key: null as string | null, message: error.message }];

  for (const { key, message } of entries) {
    if (key !== null && isFormField(key) && !fieldErrors[key]) {
      // Backend nêu tên trường JSON trong câu lỗi số. Ô số dùng câu của client, cùng khoảng giá trị với backend.
      fieldErrors[key] = NUMBER_RULES.find((rule) => rule.field === key)?.message ?? message;
    } else {
      unmatched.push(message);
    }
  }

  return { fieldErrors, unmatched };
}
