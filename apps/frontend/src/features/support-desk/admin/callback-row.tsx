import { ErrorAlert } from '../../../shared/error-alert';
import { formatVietnamDateTime } from '../../../shared/vietnam-time';
import { DELETED_PHONE_LABEL, STATUS_LABELS, phoneOrDeletedLabel } from './callback-labels';
import { PENDING_CALLBACK_STATUS, callbackPhoneHref } from './queue-view';
import type { HandledCallbackStatus } from './support-desk-api';
import type { CallbackRequest } from './support-desk-schemas';

// Nhãn trạng thái của một dòng đã xử lý. Dòng `pending` không có nhãn.
function handledLabel(status: CallbackRequest['status']): string | null {
  return status === PENDING_CALLBACK_STATUS ? null : STATUS_LABELS[status];
}

function PhoneValue({ phone }: { phone: string | null }) {
  if (phone === null)
    return <span className="support-callback-phone-deleted">{DELETED_PHONE_LABEL}</span>;
  const href = callbackPhoneHref(phone);
  // Chuỗi không còn chữ số nào thì không có số để gọi, nên hiện chữ thường thay vì link hỏng.
  if (href === null) {
    return (
      <span className="support-callback-phone support-callback-phone-plain wrap-anywhere">
        {phone}
      </span>
    );
  }
  return (
    <a className="support-callback-phone wrap-anywhere" href={href}>
      {phone}
    </a>
  );
}

interface CallbackRowProps {
  request: CallbackRequest;
  canMark: boolean;
  savingStatus: HandledCallbackStatus | null;
  errorMessage: string | null;
  onMark: (request: CallbackRequest, status: HandledCallbackStatus) => void;
}

// Một dòng yêu cầu gọi lại. Dòng chỉ vẽ dữ liệu và gọi `onMark`, trạng thái lưu nằm ở nơi gọi.
export function CallbackRow({
  request,
  canMark,
  savingStatus,
  errorMessage,
  onMark,
}: CallbackRowProps) {
  const label = handledLabel(request.status);
  const isSaving = savingStatus !== null;
  const phoneLabel = phoneOrDeletedLabel(request);

  return (
    <li className={label === null ? 'support-callback' : 'support-callback is-handled'}>
      <div className="support-callback-head">
        <PhoneValue phone={request.phone} />
        {label !== null && (
          <span className={request.status === 'done' ? 'badge badge-ok' : 'badge badge-warn'}>
            {label}
          </span>
        )}
      </div>
      {/* Ghi chú của khách là dữ liệu bên ngoài, nên chỉ vẽ bằng text node của React. */}
      {request.note !== null && request.note !== '' && (
        <p className="support-callback-note wrap-anywhere">{request.note}</p>
      )}
      <span className="cell-sub">Gửi lúc {formatVietnamDateTime(request.createdAt)}</span>
      {request.handledAt !== null && (
        <span className="cell-sub">Xử lý lúc {formatVietnamDateTime(request.handledAt)}</span>
      )}
      {/* Người thiếu `conversations:reply` chỉ xem. Dòng đã xử lý không có thao tác nào. */}
      {canMark && request.status === PENDING_CALLBACK_STATUS && (
        <div className="support-callback-actions">
          <button
            type="button"
            className="btn btn-sm"
            aria-label={`Đánh dấu đã gọi: ${phoneLabel}`}
            disabled={isSaving}
            onClick={() => onMark(request, 'done')}
          >
            {savingStatus === 'done' ? 'Đang lưu...' : STATUS_LABELS.done}
          </button>
          <button
            type="button"
            className="btn btn-sm btn-outline"
            aria-label={`Đánh dấu không liên lạc được: ${phoneLabel}`}
            disabled={isSaving}
            onClick={() => onMark(request, 'unreachable')}
          >
            {savingStatus === 'unreachable' ? 'Đang lưu...' : STATUS_LABELS.unreachable}
          </button>
        </div>
      )}
      {errorMessage !== null && (
        <ErrorAlert className="support-callback-error" message={errorMessage} />
      )}
    </li>
  );
}
