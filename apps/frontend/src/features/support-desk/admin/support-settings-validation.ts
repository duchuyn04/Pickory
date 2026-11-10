import type { ApiError } from '../../../shared/api';
import type { FieldErrors } from '../../../shared/field-error';
import { mapValidationErrors, type MappedFieldErrors } from './server-field-errors';

export type SupportSettingsFieldName =
  'opensAt' | 'closesAt' | 'waitNudgeMinutes' | 'autoCloseMinutes';

export type SupportSettingsFieldErrors = FieldErrors<SupportSettingsFieldName>;

// Thứ tự ô trên màn. Ô sai đầu tiên theo thứ tự này nhận focus.
export const SUPPORT_SETTINGS_FIELD_ORDER: readonly SupportSettingsFieldName[] = [
  'opensAt',
  'closesAt',
  'waitNudgeMinutes',
  'autoCloseMinutes',
];

// Ô số giữ chuỗi để phân biệt ô trống, số lẻ và số hợp lệ.
export interface SupportSettingsFormValues {
  opensAt: string;
  closesAt: string;
  waitNudgeMinutes: string;
  autoCloseMinutes: string;
}

export const MIN_MINUTES = 1;
export const MAX_MINUTES = 240;

export const OPENS_BEFORE_CLOSES_ERROR = 'Giờ mở cửa phải trước giờ đóng cửa.';
export const NUDGE_BEFORE_AUTO_CLOSE_ERROR = 'Mốc nhắc lại phải nhỏ hơn mốc tự đóng.';
const MINUTES_RANGE_ERROR = `Nhập số nguyên từ ${MIN_MINUTES} đến ${MAX_MINUTES}.`;

// Trả số phút khi chuỗi là số nguyên trong khoảng cho phép. Số lẻ, chữ và ô trống trả `null`.
export function parseMinutes(text: string): number | null {
  const trimmed = text.trim();
  if (!/^\d+$/.test(trimmed)) return null;
  const minutes = Number(trimmed);
  if (minutes < MIN_MINUTES || minutes > MAX_MINUTES) return null;
  return minutes;
}

// Kiểm trước khi gửi để lỗi hiện dưới đúng ô mà không tốn một request. Backend vẫn kiểm lại toàn bộ.
export function validateSupportSettings(
  values: SupportSettingsFormValues,
): SupportSettingsFieldErrors {
  const errors: SupportSettingsFieldErrors = {};

  if (values.opensAt === '') errors.opensAt = 'Hãy chọn giờ mở cửa.';
  if (values.closesAt === '') errors.closesAt = 'Hãy chọn giờ đóng cửa.';
  // Chuỗi `HH:mm` cùng độ dài nên so chuỗi cho đúng thứ tự giờ. Giờ bằng nhau cũng là lỗi.
  if (!errors.opensAt && !errors.closesAt && values.opensAt >= values.closesAt) {
    errors.opensAt = OPENS_BEFORE_CLOSES_ERROR;
  }

  const nudgeMinutes = parseMinutes(values.waitNudgeMinutes);
  const autoCloseMinutes = parseMinutes(values.autoCloseMinutes);
  if (nudgeMinutes === null) errors.waitNudgeMinutes = MINUTES_RANGE_ERROR;
  if (autoCloseMinutes === null) errors.autoCloseMinutes = MINUTES_RANGE_ERROR;
  // Mốc nhắc lại bằng mốc tự đóng cũng là lỗi: phiên đóng cùng lúc nhắc, nên lời nhắc vô nghĩa.
  if (nudgeMinutes !== null && autoCloseMinutes !== null && nudgeMinutes >= autoCloseMinutes) {
    errors.waitNudgeMinutes = NUDGE_BEFORE_AUTO_CLOSE_ERROR;
  }

  return errors;
}

// Khóa của backend (tên trường JSON) và ô tương ứng. Hai tên trùng nhau nên bảng ghi rõ để khóa lạ không lọt vào.
const SERVER_KEY_TO_FIELD: Record<string, SupportSettingsFieldName> = {
  opensAt: 'opensAt',
  closesAt: 'closesAt',
  waitNudgeMinutes: 'waitNudgeMinutes',
  autoCloseMinutes: 'autoCloseMinutes',
};

export function mapSupportSettingsServerErrors(
  error: ApiError,
): MappedFieldErrors<SupportSettingsFieldName> {
  return mapValidationErrors(error, SERVER_KEY_TO_FIELD);
}
