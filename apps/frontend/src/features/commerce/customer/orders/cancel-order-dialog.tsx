import { useRef, useState } from 'react';
import { useDialogFocus } from '../../../../shared/use-dialog-focus';
import { ErrorAlert } from '../../../../shared/error-alert';
import { ApiError } from '../../../../shared/api';

interface CancelOrderDialogProps {
  code: string;
  onConfirm: () => Promise<void>;
  onClose: () => void;
}

// Hộp thoại xác nhận hủy đơn hàng yêu cầu khách hàng xác nhận thao tác.
export function CancelOrderDialog({ code, onConfirm, onClose }: CancelOrderDialogProps) {
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const isSubmittingRef = useRef(false);

  // Chặn đóng hộp thoại khi yêu cầu hủy đang được gửi lên máy chủ.
  function closeUnlessSubmitting() {
    if (isSubmitting) return;
    onClose();
  }

  // Quản lý tiêu điểm bàn phím và phím Escape để đóng hộp thoại an toàn.
  const dialogRef = useDialogFocus({ onClose: closeUnlessSubmitting });

  async function handleConfirm() {
    // Chặn gửi nhiều yêu cầu đồng thời khi người dùng bấm đúp chuột.
    if (isSubmittingRef.current) return;
    isSubmittingRef.current = true;
    setErrorMessage(null);
    setIsSubmitting(true);

    try {
      await onConfirm();
    } catch (error) {
      // Mở lại cờ gửi để khách có thể thao tác lại sau lỗi mạng tạm thời.
      isSubmittingRef.current = false;
      setIsSubmitting(false);
      const apiMessage = error instanceof ApiError ? error.message : 'Không thể hủy đơn hàng.';
      setErrorMessage(apiMessage);
    }
  }

  return (
    <div className="dialog-backdrop">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="cancel-order-title"
        aria-describedby="cancel-order-description"
        tabIndex={-1}
        className="dialog"
      >
        <div className="dialog-head">
          <h2 id="cancel-order-title">Xác nhận hủy đơn hàng</h2>
        </div>
        <div className="dialog-body stack">
          {errorMessage && <ErrorAlert message={errorMessage} />}
          <p id="cancel-order-description">
            Bạn có chắc chắn muốn hủy đơn hàng <strong>{code}</strong> không? Đơn đã hủy không khôi
            phục được.
          </p>
        </div>
        <div className="dialog-foot">
          <button
            type="button"
            className="btn btn-outline"
            onClick={closeUnlessSubmitting}
            disabled={isSubmitting}
          >
            Giữ đơn
          </button>
          <button
            type="button"
            className="btn btn-danger"
            onClick={handleConfirm}
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang hủy...' : 'Hủy đơn'}
          </button>
        </div>
      </div>
    </div>
  );
}
