import { useState, useId, type FormEvent } from 'react';
import { z } from 'zod';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { FieldError, fieldErrorProps } from '../../../shared/field-error';
import { Icon } from '../../../shared/icons';
import { getPhoneError } from '../../../shared/identity-validation';
import { sendCallbackRequest } from './support-customer-api';

// Chuẩn hóa số điện thoại Việt Nam về dạng duy nhất 0xxxxxxxxx để gửi API.
export function normalizePhoneNumber(raw: string): string | null {
  // Trả null khi số trống để không gửi chuỗi rỗng lên API.
  if (!raw.trim()) {
    return null;
  }
  // Chỉ chuẩn hóa khi chuỗi số đáp ứng đúng định dạng của hệ thống.
  if (getPhoneError(raw) !== null) {
    return null;
  }
  // Khách hay chia nhóm số bằng khoảng trắng, dấu chấm và dấu gạch. Loại bỏ ký tự phân tách này.
  const compact = raw.replace(/[\s.-]/g, '');
  // API chỉ nhận một dạng số 0xxxxxxxxx. Chuyển tiền tố mã quốc gia +84 về số 0.
  if (compact.startsWith('+84')) {
    return `0${compact.slice(3)}`;
  }
  return compact;
}

export const callbackRequestFormSchema = z.object({
  phone: z.string().superRefine((val, ctx) => {
    // Bắt buộc nhập số điện thoại trước khi gửi yêu cầu gọi lại.
    if (!val.trim()) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Số điện thoại không được để trống.',
      });
      return;
    }
    // Dùng luật kiểm tra định dạng số điện thoại chung của hệ thống.
    const phoneError = getPhoneError(val);
    if (phoneError !== null) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: phoneError,
      });
    }
  }),
  note: z.string().max(500, 'Ghi chú tối đa 500 ký tự.'),
});

interface CallbackRequestFormProps {
  conversationId: string | null;
}

interface FormFieldErrors {
  phone?: string;
  note?: string;
}

// Form để khách để lại số điện thoại và ghi chú hẹn gọi lại.
export function CallbackRequestForm({ conversationId }: CallbackRequestFormProps) {
  // Một hội thoại có thể có nhiều tin `callback_form`. Mỗi form cần id riêng để nhãn và lỗi không trỏ nhầm.
  const formId = useId();
  const phoneInputId = `${formId}-phone`;
  const noteInputId = `${formId}-note`;
  const [phone, setPhone] = useState('');
  const [note, setNote] = useState('');
  const [fieldErrors, setFieldErrors] = useState<FormFieldErrors>({});
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSubmitted, setIsSubmitted] = useState(false);
  const [isLocked, setIsLocked] = useState(false);

  // Gửi thành công thì thay toàn bộ form bằng thông báo xác nhận.
  if (isSubmitted) {
    return (
      <div className="support-message-action support-form-card" data-action="callback_form">
        <div role="status" className="alert alert-list alert-ok">
          <Icon name="check" className="icon alert-list-icon" />
          <div className="alert-list-body">
            <div className="alert-list-title">
              Yêu cầu gọi lại đã được ghi nhận. Nhân viên CSKH sẽ liên hệ với bạn sớm nhất.
            </div>
          </div>
        </div>
      </div>
    );
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    // Khóa form sau lỗi 429 hoặc chặn bấm nhiều lần trong lúc gửi.
    if (isLocked || isSubmitting) {
      return;
    }

    // Không gửi request khi chưa có mã hội thoại hợp lệ.
    if (!conversationId) {
      setSubmitError('Chưa xác định được hội thoại. Hãy tải lại trang.');
      return;
    }

    // Kiểm tra tính hợp lệ của toàn bộ form trước khi gửi API.
    const validationResult = callbackRequestFormSchema.safeParse({ phone, note });
    if (!validationResult.success) {
      const nextErrors: FormFieldErrors = {};
      for (const issue of validationResult.error.issues) {
        const fieldName = issue.path[0];
        if (fieldName === 'phone' && !nextErrors.phone) {
          nextErrors.phone = issue.message;
        } else if (fieldName === 'note' && !nextErrors.note) {
          nextErrors.note = issue.message;
        }
      }
      setFieldErrors(nextErrors);
      return;
    }

    setFieldErrors({});
    setSubmitError(null);
    setIsSubmitting(true);

    try {
      const normalizedPhone = normalizePhoneNumber(phone)!;
      const normalizedNote = note.trim() ? note.trim() : null;

      // Gọi API tạo yêu cầu gọi lại với dữ liệu đã chuẩn hóa.
      await sendCallbackRequest({
        conversationId,
        phone: normalizedPhone,
        note: normalizedNote,
      });

      setIsSubmitted(true);
    } catch (error) {
      if (error instanceof ApiError) {
        setSubmitError(error.message);
        // Khi nhận lỗi 429, khóa form và không cho phép khách gửi lại.
        if (error.status === 429 || error.code === 'CALLBACK_LIMIT_REACHED') {
          setIsLocked(true);
        }
      } else {
        setSubmitError('Đã có lỗi xảy ra khi gửi yêu cầu. Hãy thử lại.');
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="support-message-action support-form-card" data-action="callback_form">
      <h3 className="support-form-title">Yêu cầu gọi lại</h3>
      <p className="support-form-desc">
        Để lại số điện thoại để nhân viên CSKH liên hệ hỗ trợ bạn sớm nhất.
      </p>

      {submitError && <ErrorAlert message={submitError} className="support-form-alert" />}

      <form onSubmit={handleSubmit} noValidate>
        <div className="field">
          <label htmlFor={phoneInputId}>
            Số điện thoại{' '}
            <span className="text-danger" aria-hidden="true">
              *
            </span>
          </label>
          <input
            id={phoneInputId}
            type="tel"
            name="phone"
            autoComplete="tel"
            placeholder="0901234567"
            disabled={isSubmitting || isLocked}
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            {...fieldErrorProps(phoneInputId, fieldErrors.phone)}
          />
          <FieldError inputId={phoneInputId} message={fieldErrors.phone} />
        </div>

        <div className="field">
          <label htmlFor={noteInputId}>Ghi chú (không bắt buộc)</label>
          <textarea
            id={noteInputId}
            name="note"
            rows={2}
            placeholder="Thời gian tiện nghe máy hoặc thông tin cần hỗ trợ..."
            disabled={isSubmitting || isLocked}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            {...fieldErrorProps(noteInputId, fieldErrors.note)}
          />
          <FieldError inputId={noteInputId} message={fieldErrors.note} />
        </div>

        <button
          type="submit"
          className="btn btn-primary btn-block"
          disabled={isSubmitting || isLocked}
        >
          {isSubmitting ? 'Đang gửi yêu cầu...' : 'Gửi yêu cầu gọi lại'}
        </button>
      </form>
    </div>
  );
}
