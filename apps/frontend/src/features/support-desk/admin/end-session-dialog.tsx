import { useRef, useState } from 'react';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import { closeSupportSession } from './support-desk-api';

interface EndSessionDialogProps {
  sessionId: string;
  onSuccess: () => void;
  onSessionClosed: () => void;
  onClose: () => void;
}

const DEFAULT_CLOSE_ERROR = 'Chưa kết thúc được hội thoại. Hãy thử lại.';

// Hộp thoại xác nhận kết thúc phiên hỗ trợ trước khi gọi API đóng phiên.
export function EndSessionDialog({
  sessionId,
  onSuccess,
  onSessionClosed,
  onClose,
}: EndSessionDialogProps) {
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const isSubmittingRef = useRef(false);

  // Chặn phím Esc và nút Hủy đóng hộp thoại khi yêu cầu đang xử lý.
  function closeUnlessSubmitting() {
    if (isSubmitting) return;
    onClose();
  }

  const dialogRef = useDialogFocus({ onClose: closeUnlessSubmitting });

  async function handleConfirm() {
    // Chặn bấm đôi đồng bộ trước khi React vẽ lại trạng thái đang gửi.
    if (isSubmittingRef.current) return;
    isSubmittingRef.current = true;
    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      // Gọi API đóng phiên sau khi nhân viên đã xác nhận trên hộp thoại.
      await closeSupportSession(sessionId);
      // Báo thành công để trang chính đánh dấu phiên đã đóng và nạp lại lịch sử tin.
      onSuccess();
    } catch (error) {
      // Nhả cờ gửi để nhân viên có thể bấm thử lại khi gặp lỗi tạm thời.
      isSubmittingRef.current = false;
      setIsSubmitting(false);

      // Khi phiên đã kết thúc trước đó, cập nhật ngay trạng thái phiên ở màn hình chính.
      if (error instanceof ApiError && error.code === 'SUPPORT_SESSION_CLOSED') {
        onSessionClosed();
        onClose();
        return;
      }

      // Dùng câu lỗi từ API nếu có để nhân viên biết nguyên nhân, hoặc dùng câu mặc định.
      const displayMessage = error instanceof ApiError ? error.message : DEFAULT_CLOSE_ERROR;
      setErrorMessage(displayMessage);
    }
  }

  return (
    <div className="dialog-backdrop">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="end-session-title"
        aria-describedby="end-session-description"
        tabIndex={-1}
        className="dialog"
      >
        <div className="dialog-head">
          <h2 id="end-session-title">Kết thúc hội thoại này?</h2>
        </div>
        <div className="dialog-body stack">
          {errorMessage && <ErrorAlert message={errorMessage} />}
          <p id="end-session-description">
            Khách hàng sẽ nhận được biểu mẫu chấm điểm dịch vụ sau khi kết thúc hội thoại.
          </p>
        </div>
        <div className="dialog-foot">
          <button
            type="button"
            className="btn btn-outline"
            onClick={closeUnlessSubmitting}
            disabled={isSubmitting}
          >
            Hủy
          </button>
          <button
            type="button"
            className="btn btn-danger"
            onClick={() => void handleConfirm()}
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang kết thúc...' : 'Kết thúc'}
          </button>
        </div>
      </div>
    </div>
  );
}
