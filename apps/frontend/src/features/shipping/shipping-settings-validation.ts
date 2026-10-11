import type { ApiError } from '../../shared/api';
import type { FieldErrors } from '../../shared/field-error';
import { getNameError, getPhoneError } from '../../shared/identity-validation';

export type ShippingFieldName =
  'senderName' | 'senderPhone' | 'province' | 'ward' | 'line' | 'serviceType';

export type ShippingFieldErrors = FieldErrors<ShippingFieldName>;

export interface ShippingFormValues {
  senderName: string;
  senderPhone: string;
  provinceCode: string;
  wardCode: string;
  line: string;
}

// Độ dài tối đa của số nhà, khớp `LineMaxLength` ở backend.
const LINE_MAX_LENGTH = 200;

const graphemeSegmenter = new Intl.Segmenter('vi', { granularity: 'grapheme' });

function hasControlCharacter(text: string): boolean {
  return [...text].some((character) => {
    const codePoint = character.codePointAt(0) ?? 0;
    return codePoint <= 0x1f || codePoint === 0x7f;
  });
}

function getLineError(line: string): string | null {
  const trimmed = line.trim();
  if (!trimmed) return 'Cần nhập số nhà, tên đường.';
  // Kiểm ký tự điều khiển trước độ dài, cùng thứ tự với backend.
  if (hasControlCharacter(trimmed)) return 'Số nhà, tên đường chứa ký tự không hợp lệ.';
  // Đếm theo ký tự người đọc thấy, giống backend.
  if ([...graphemeSegmenter.segment(trimmed)].length > LINE_MAX_LENGTH) {
    return `Số nhà, tên đường tối đa ${LINE_MAX_LENGTH} ký tự.`;
  }
  return null;
}

// Kiểm trước khi gửi để lỗi hiện dưới đúng ô mà không tốn một request. Backend vẫn kiểm lại toàn bộ.
export function validateShippingForm(values: ShippingFormValues): ShippingFieldErrors {
  const errors: ShippingFieldErrors = {};

  // Luật tên và số điện thoại của hồ sơ cho phép trống, nên bắt buộc kiểm riêng ở đây.
  if (!values.senderName.trim()) {
    errors.senderName = 'Cần nhập tên người gửi.';
  } else {
    errors.senderName = getNameError(values.senderName);
  }

  if (!values.senderPhone.trim()) {
    errors.senderPhone = 'Cần nhập số điện thoại người gửi.';
  } else {
    errors.senderPhone = getPhoneError(values.senderPhone);
  }

  if (!values.provinceCode) errors.province = 'Cần chọn tỉnh/thành phố.';
  if (!values.wardCode) errors.ward = 'Cần chọn phường/xã.';
  errors.line = getLineError(values.line);

  // Bỏ khóa không có lỗi để kết quả chỉ chứa ô sai.
  return Object.fromEntries(
    Object.entries(errors).filter(([, message]) => Boolean(message)),
  ) as ShippingFieldErrors;
}

export interface MappedServerErrors {
  fieldErrors: ShippingFieldErrors;
  // Lỗi không gắn được vào ô nào của form. Màn hiện chúng bằng `ErrorAlert` đầu form.
  unmatched: string[];
}

// Khóa của backend (tên trường JSON) và ô tương ứng trong form.
const SERVER_KEY_TO_FIELD: Record<string, ShippingFieldName> = {
  senderName: 'senderName',
  senderPhone: 'senderPhone',
  provinceCode: 'province',
  wardCode: 'ward',
  line: 'line',
  defaultServiceTypeId: 'serviceType',
};

// Lấy danh sách `khóa: lỗi` từ `details` có cấu trúc của backend. Câu `message` là chữ cho người đọc,
// nên không đọc ngược câu đó để lấy khóa.
function readKeyedMessages(error: ApiError): { key: string | null; message: string }[] {
  if (error.details && error.details.length > 0) {
    return error.details.map((detail) => ({ key: detail.key, message: detail.message }));
  }

  // Lỗi không có `details` (500, 503, mạng) hiện nguyên câu `message` ở đầu form.
  return [{ key: null, message: error.message }];
}

// Gán lỗi của backend vào đúng ô. Khóa không thuộc form (ví dụ trường lạ hay lỗi cả body) vào `unmatched`.
export function mapServerErrors(error: ApiError): MappedServerErrors {
  const fieldErrors: ShippingFieldErrors = {};
  const unmatched: string[] = [];

  for (const { key, message } of readKeyedMessages(error)) {
    const field = key === null ? undefined : SERVER_KEY_TO_FIELD[key];
    if (field && !fieldErrors[field]) {
      fieldErrors[field] = message;
    } else {
      unmatched.push(message);
    }
  }

  return { fieldErrors, unmatched };
}
