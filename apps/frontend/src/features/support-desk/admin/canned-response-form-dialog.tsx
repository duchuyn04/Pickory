import { useRef, useState, type FormEvent } from 'react';
import { AdminFormDialog } from '../../../shared/admin-form-dialog';
import { ApiError } from '../../../shared/api';
import { getErrorMessage } from '../../../shared/error-message';
import { FieldError, fieldPropsWithHint, hasFieldErrors } from '../../../shared/field-error';
import { useBodyScrollLock } from '../../../shared/use-body-scroll-lock';
import {
  BODY_MAX_LENGTH,
  mapCannedResponseServerErrors,
  SHORTCUT_MAX_LENGTH,
  SHORTCUT_MIN_LENGTH,
  stripLeadingSlashes,
  validateCannedResponse,
  type CannedResponseFieldErrors,
} from './canned-response-validation';
import {
  createCannedResponse,
  updateCannedResponse,
  type UpdateCannedResponseInput,
} from './support-desk-api';
import type { CannedResponse } from './support-desk-schemas';

export type CannedResponseFormTarget =
  { mode: 'create' } | { mode: 'edit'; response: CannedResponse };

interface CannedResponseFormDialogProps {
  target: CannedResponseFormTarget;
  // `true` khi biểu mẫu tạo mẫu dùng chung, `false` khi tạo mẫu cá nhân.
  isShared: boolean;
  onClose: () => void;
  // Báo mẫu đã lưu để nơi gọi đọc lại danh sách và hiện thông báo.
  onSaved: (saved: CannedResponse) => void;
  // Báo mẫu đã bị xóa ở nơi khác. Nơi gọi hiện câu của API và đọc lại danh sách.
  onMissing: (message: string) => void;
}

const SHORTCUT_INPUT_ID = 'canned-response-shortcut';
const BODY_INPUT_ID = 'canned-response-body';
const SHORTCUT_HINT_ID = 'canned-response-shortcut-hint';
const BODY_COUNTER_ID = 'canned-response-body-counter';
const FORM_ID = 'canned-response-form';
const DEFAULT_SAVE_ERROR = 'Chưa lưu được mẫu. Hãy thử lại.';

// Hộp thoại thêm hoặc sửa một mẫu. Dữ liệu đã nhập được giữ nguyên khi lưu lỗi.
export function CannedResponseFormDialog({
  target,
  isShared,
  onClose,
  onSaved,
  onMissing,
}: CannedResponseFormDialogProps) {
  const original = target.mode === 'edit' ? target.response : null;
  const [shortcut, setShortcut] = useState(original?.shortcut ?? '');
  const [body, setBody] = useState(original?.body ?? '');
  const [fieldErrors, setFieldErrors] = useState<CannedResponseFieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  // Cờ đồng bộ chặn bấm đôi, vì `isSubmitting` chỉ đổi sau lần vẽ lại kế tiếp.
  const isSubmittingRef = useRef(false);
  useBodyScrollLock(true);

  const bodyLength = body.trim().length;

  function focusFirstInvalidField(errors: CannedResponseFieldErrors) {
    if (errors.shortcut) document.getElementById(SHORTCUT_INPUT_ID)?.focus();
    else if (errors.body) document.getElementById(BODY_INPUT_ID)?.focus();
  }

  // Chỉ gửi trường khác giá trị cũ. Gửi cả trường không đổi có thể đụng luật phím tắt trùng của chính mẫu này.
  function buildChanges(
    cleanShortcut: string,
    cleanBody: string,
    previous: CannedResponse,
  ): UpdateCannedResponseInput {
    const changes: UpdateCannedResponseInput = {};
    if (cleanShortcut !== previous.shortcut) changes.shortcut = cleanShortcut;
    if (cleanBody !== previous.body.trim()) changes.body = cleanBody;
    return changes;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    // Phím Enter trong ô phím tắt vẫn gửi form khi nút đã khóa. Chặn tại đây để không tạo hai mẫu.
    if (isSubmittingRef.current) return;
    setFormError(null);

    // Kiểm phía client trước. Còn lỗi thì không gửi request, để lỗi hiện ngay dưới đúng ô.
    const clientErrors = validateCannedResponse({ shortcut, body });
    setFieldErrors(clientErrors);
    if (hasFieldErrors(clientErrors)) {
      focusFirstInvalidField(clientErrors);
      return;
    }

    const cleanShortcut = stripLeadingSlashes(shortcut.trim());
    const cleanBody = body.trim();

    // Sửa mà không đổi gì thì đóng hộp thoại, không tốn request và không đổi thời điểm cập nhật của mẫu.
    let changes: UpdateCannedResponseInput | null = null;
    if (original) {
      changes = buildChanges(cleanShortcut, cleanBody, original);
      if (Object.keys(changes).length === 0) {
        onClose();
        return;
      }
    }

    isSubmittingRef.current = true;
    setIsSubmitting(true);
    try {
      const saved =
        original && changes
          ? await updateCannedResponse(original.id, changes)
          : await createCannedResponse({
              shortcut: cleanShortcut,
              body: cleanBody,
              shared: isShared,
            });
      onSaved(saved);
    } catch (error) {
      handleSaveError(error);
    } finally {
      isSubmittingRef.current = false;
      setIsSubmitting(false);
    }
  }

  // Esc, nút Hủy và nút đóng không đóng hộp thoại khi lệnh lưu đang chạy. Nếu đóng, lỗi từ máy chủ mất và kết quả cũ có thể đóng hộp mới.
  function closeUnlessSubmitting() {
    if (isSubmittingRef.current) return;
    onClose();
  }

  function handleSaveError(error: unknown) {
    if (!(error instanceof ApiError)) {
      setFormError(getErrorMessage(error, DEFAULT_SAVE_ERROR));
      return;
    }
    // Phím tắt đã có thì lỗi thuộc ô phím tắt, để nhân viên sửa đúng chỗ.
    if (error.code === 'CANNED_SHORTCUT_TAKEN') {
      setFieldErrors({ shortcut: error.message });
      focusFirstInvalidField({ shortcut: error.message });
      return;
    }
    // Mẫu đã bị xóa ở nơi khác thì không sửa tiếp được. Đóng hộp thoại để danh sách đọc lại.
    if (error.code === 'CANNED_RESPONSE_NOT_FOUND') {
      onMissing(error.message);
      return;
    }
    if (error.status === 400) {
      // Lỗi 400 gắn vào đúng ô. Phần không gắn được vào ô nào hiện đầu hộp thoại.
      const mapped = mapCannedResponseServerErrors(error);
      setFieldErrors(mapped.fieldErrors);
      setFormError(mapped.unmatched.length > 0 ? mapped.unmatched.join(' ') : null);
      focusFirstInvalidField(mapped.fieldErrors);
      return;
    }
    setFormError(error.message);
  }

  return (
    <AdminFormDialog
      isOpen
      onClose={closeUnlessSubmitting}
      title={original ? `Sửa mẫu /${original.shortcut}` : 'Thêm mẫu'}
      formId={FORM_ID}
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      closeDisabled={isSubmitting}
      submitText="Lưu mẫu"
      error={formError}
    >
      <div className="field">
        <label htmlFor={SHORTCUT_INPUT_ID}>Phím tắt</label>
        <div className="canned-shortcut-input">
          {/* Dấu `/` cố định bên cạnh ô, nên người dùng không gõ dấu này. */}
          <span className="canned-shortcut-prefix" aria-hidden="true">
            /
          </span>
          <input
            id={SHORTCUT_INPUT_ID}
            type="text"
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            value={shortcut}
            onChange={(event) => {
              setShortcut(stripLeadingSlashes(event.target.value));
              setFieldErrors((current) => ({ ...current, shortcut: null }));
            }}
            {...fieldPropsWithHint(SHORTCUT_INPUT_ID, SHORTCUT_HINT_ID, fieldErrors.shortcut)}
          />
        </div>
        <FieldError inputId={SHORTCUT_INPUT_ID} message={fieldErrors.shortcut} />
        <p id={SHORTCUT_HINT_ID} className="hint">
          Dài {SHORTCUT_MIN_LENGTH} đến {SHORTCUT_MAX_LENGTH} ký tự: chữ thường không dấu, số, dấu -
          và dấu _.
        </p>
      </div>
      <div className="field">
        <label htmlFor={BODY_INPUT_ID}>Nội dung</label>
        <textarea
          id={BODY_INPUT_ID}
          rows={5}
          value={body}
          onChange={(event) => {
            setBody(event.target.value);
            setFieldErrors((current) => ({ ...current, body: null }));
          }}
          {...fieldPropsWithHint(BODY_INPUT_ID, BODY_COUNTER_ID, fieldErrors.body)}
        />
        <FieldError inputId={BODY_INPUT_ID} message={fieldErrors.body} />
        <p
          id={BODY_COUNTER_ID}
          className={bodyLength > BODY_MAX_LENGTH ? 'canned-counter is-over' : 'canned-counter'}
        >
          {bodyLength}/{BODY_MAX_LENGTH}
        </p>
      </div>
    </AdminFormDialog>
  );
}
