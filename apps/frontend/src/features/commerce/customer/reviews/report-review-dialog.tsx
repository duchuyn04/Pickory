import { useRef, useState } from 'react';
import { ApiError } from '../../../../shared/api';
import { ErrorAlert } from '../../../../shared/error-alert';
import { useDialogFocus } from '../../../../shared/use-dialog-focus';

interface ReportReviewDialogProps {
  onConfirm: () => Promise<void>;
  onClose: () => void;
}

// Hộp thoại xác nhận báo cáo một đánh giá. Lỗi khi gửi hiện ngay trong hộp thoại.
export function ReportReviewDialog({ onConfirm, onClose }: ReportReviewDialogProps) {
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const isSubmittingRef = useRef(false);

  // Không đóng hộp thoại khi báo cáo đang được gửi, để khách thấy kết quả của lần gửi đó.
  function closeUnlessSubmitting() {
    if (isSubmitting) return;
    onClose();
  }

  const dialogRef = useDialogFocus({ onClose: closeUnlessSubmitting });

  async function handleConfirm() {
    // Chặn bấm đúp. Hai báo cáo cùng lúc làm lần thứ hai nhận 409.
    if (isSubmittingRef.current) return;
    isSubmittingRef.current = true;
    setErrorMessage(null);
    setIsSubmitting(true);

    try {
      await onConfirm();
    } catch (error: unknown) {
      // Mở lại cờ gửi để khách thử lại sau lỗi mạng tạm thời.
      isSubmittingRef.current = false;
      setIsSubmitting(false);
      setErrorMessage(error instanceof ApiError ? error.message : 'Không thể gửi báo cáo.');
    }
  }

  return (
    <div className="dialog-backdrop">
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="report-review-title"
        aria-describedby="report-review-description"
        tabIndex={-1}
        className="dialog"
      >
        <div className="dialog-head">
          <h2 id="report-review-title">Báo cáo đánh giá</h2>
        </div>
        <div className="dialog-body stack">
          {errorMessage && <ErrorAlert message={errorMessage} />}
          <p id="report-review-description">
            Gửi báo cáo cho nhân viên kiểm duyệt nếu đánh giá này vi phạm quy định. Mỗi khách báo
            cáo một đánh giá tối đa một lần.
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
            onClick={handleConfirm}
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang gửi...' : 'Gửi báo cáo'}
          </button>
        </div>
      </div>
    </div>
  );
}
