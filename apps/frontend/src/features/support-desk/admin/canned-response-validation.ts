import type { ApiError } from '../../../shared/api';
import type { FieldErrors } from '../../../shared/field-error';
import { mapValidationErrors, type MappedFieldErrors } from './server-field-errors';

export type CannedResponseFieldName = 'shortcut' | 'body';

export type CannedResponseFieldErrors = FieldErrors<CannedResponseFieldName>;

export interface CannedResponseFormValues {
  shortcut: string;
  body: string;
}

export const SHORTCUT_MIN_LENGTH = 2;
export const SHORTCUT_MAX_LENGTH = 30;
export const BODY_MAX_LENGTH = 2000;

// Chỉ chữ thường không dấu, số, `-` và `_`, theo mục "Giới hạn" của hợp đồng.
const SHORTCUT_CHARACTERS = /^[a-z0-9_-]+$/;

const SHORTCUT_EMPTY_ERROR = 'Hãy nhập phím tắt.';
const SHORTCUT_LENGTH_ERROR = `Phím tắt dài từ ${SHORTCUT_MIN_LENGTH} đến ${SHORTCUT_MAX_LENGTH} ký tự.`;
const SHORTCUT_CHARACTER_ERROR = 'Phím tắt chỉ gồm chữ thường không dấu, số, dấu - và dấu _.';
const BODY_EMPTY_ERROR = 'Hãy nhập nội dung mẫu.';
const BODY_LENGTH_ERROR = `Nội dung tối đa ${BODY_MAX_LENGTH.toLocaleString('vi-VN')} ký tự.`;

// Ô phím tắt đã có dấu `/` cố định bên cạnh, nên dấu `/` người dùng gõ hoặc dán vào đầu chuỗi bị bỏ.
export function stripLeadingSlashes(shortcut: string): string {
  return shortcut.replace(/^\/+/, '');
}

function getShortcutError(shortcut: string): string | null {
  if (shortcut === '') return SHORTCUT_EMPTY_ERROR;
  // Đếm bằng `length` vì ký tự hợp lệ đều là ASCII. Ký tự ngoài ASCII đã sai luật ký tự.
  if (shortcut.length < SHORTCUT_MIN_LENGTH || shortcut.length > SHORTCUT_MAX_LENGTH) {
    return SHORTCUT_LENGTH_ERROR;
  }
  if (!SHORTCUT_CHARACTERS.test(shortcut)) return SHORTCUT_CHARACTER_ERROR;
  return null;
}

function getBodyError(body: string): string | null {
  const trimmedBody = body.trim();
  if (trimmedBody === '') return BODY_EMPTY_ERROR;
  if (trimmedBody.length > BODY_MAX_LENGTH) return BODY_LENGTH_ERROR;
  return null;
}

// Kiểm trước khi gửi để lỗi hiện dưới đúng ô mà không tốn một request. Backend vẫn kiểm lại toàn bộ.
export function validateCannedResponse(
  values: CannedResponseFormValues,
): CannedResponseFieldErrors {
  const errors: CannedResponseFieldErrors = {};
  const shortcutError = getShortcutError(stripLeadingSlashes(values.shortcut.trim()));
  const bodyError = getBodyError(values.body);
  if (shortcutError) errors.shortcut = shortcutError;
  if (bodyError) errors.body = bodyError;
  return errors;
}

const SERVER_KEY_TO_FIELD: Record<string, CannedResponseFieldName> = {
  shortcut: 'shortcut',
  body: 'body',
};

export function mapCannedResponseServerErrors(
  error: ApiError,
): MappedFieldErrors<CannedResponseFieldName> {
  return mapValidationErrors(error, SERVER_KEY_TO_FIELD);
}
