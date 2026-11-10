import { useRef, useState } from 'react';
import { ApiError } from '../../../shared/api';
import { ErrorAlert } from '../../../shared/error-alert';
import { getErrorMessage } from '../../../shared/error-message';
import { useBodyScrollLock } from '../../../shared/use-body-scroll-lock';
import { useDialogFocus } from '../../../shared/use-dialog-focus';
import { deleteCannedResponse } from './support-desk-api';
import type { CannedResponse } from './support-desk-schemas';

interface DeleteCannedResponseDialogProps {
  response: CannedResponse;
  onClose: () => void;
  onDeleted: () => void;
  // Báo mẫu đã bị xóa ở nơi khác. Nơi gọi hiện câu của API và đọc lại danh sách.
  onMissing: (message: string) => void;
}

const DEFAULT_DELETE_ERROR = 'Chưa xóa được mẫu. Hãy thử lại.';

// Hộp thoại xác nhận trước khi xóa mẫu, vì xóa xong nhân viên không chèn được mẫu này nữa.
export function DeleteCannedResponseDialog({
  response,
  onClose,
  onDeleted,
  onMissing,
}: DeleteCannedResponseDialogProps) {
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  // Cờ đồng bộ chặn bấm đôi, vì `isSubmitting` chỉ đổi sau lần vẽ lại kế tiếp.
  const isSubmittingRef = useRef(false);
  useBodyScrollLock(true);

  // Esc và nút Hủy không đóng hộp thoại khi lệnh xóa đang chạy, để kết quả không rơi vào hộp đã đóng.
  // Đọc ref thay vì state, vì state chỉ đổi sau lần vẽ lại kế tiếp.
  function closeUnlessSubmitting() {
    if (isSubmittingRef.current) return;
    onClose();
  }

  const dialogRef = useDialogFocus({ onClose: closeUnlessSubmitting });

  async function handleConfirm() {
    if (isSubmittingRef.current) return;
    isSubmittingRef.current = true;
    setIsSubmitting(true);
    setErrorMessage(null);
    try {
      await deleteCannedResponse(response.id);
      onDeleted();
    } catch (error) {
      // Mẫu đã bị xóa ở nơi khác thì mục tiêu của thao tác đã đạt. Danh sách đọc lại để hết dòng cũ.
      if (error instanceof ApiError && error.code === 'CANNED_RESPONSE_NOT_FOUND') {
        onMissing(error.message);
        return;
      }
      setErrorMessage(getErrorMessage(error, DEFAULT_DELETE_ERROR));
    } finally {
      // Nhả cờ gửi để nhân viên bấm thử lại được khi gặp lỗi tạm thời.
      isSubmittingRef.current = false;
      setIsSubmitting(false);
    }
  }

  return (
    <div className="dialog-backdrop">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="delete-canned-title"
        aria-describedby="delete-canned-description"
        tabIndex={-1}
        className="dialog"
      >
        <div className="dialog-head">
          <h2 id="delete-canned-title">Xóa mẫu /{response.shortcut}?</h2>
        </div>
        <div className="dialog-body stack">
          {errorMessage && <ErrorAlert message={errorMessage} />}
          <p id="delete-canned-description">
            Nhân viên không còn chèn được mẫu này khi trả lời khách.
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
            {isSubmitting ? 'Đang xóa...' : 'Xóa mẫu'}
          </button>
        </div>
      </div>
    </div>
  );
}
