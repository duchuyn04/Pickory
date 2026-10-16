import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type FormEvent } from 'react';
import { AdminFormDialog } from '../../../../shared/admin-form-dialog';
import { ApiError } from '../../../../shared/api';
import { FieldError } from '../../../../shared/field-error';
import { StarRating } from '../../customer/reviews/star-rating';
import { moderateReview, type AdminReview } from './admin-reviews-api';
import {
  formatReasonLabel,
  reasonCodesFor,
  validateReasonChoice,
  type ModerationAction,
} from './review-moderation-logic';

const REASON_FIELD_ID = 'review-moderation-reason';
const RADIO_GROUP_NAME = 'review-moderation-reason';

// Khóa truy vấn của hàng chờ. Sau mỗi lần ẩn hoặc hiện, mọi trang và tab của hàng chờ đều cũ.
export const ADMIN_REVIEWS_KEY = ['admin-reviews'] as const;

interface ReviewModerationDialogProps {
  review: AdminReview;
  action: ModerationAction;
  onClose: () => void;
  onDone: (message: string) => void;
}

// Hộp thoại ẩn hoặc hiện lại một đánh giá. Nhân viên phải chọn một lý do trong danh sách của hành động.
export function ReviewModerationDialog({
  review,
  action,
  onClose,
  onDone,
}: ReviewModerationDialogProps) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string | null>(null);
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  // Cờ đồng bộ chặn bấm đúp. State `isSubmitting` chỉ cập nhật sau lần render kế tiếp.
  const isSubmittingRef = useRef(false);
  const isHiding = action === 'hide';

  function refreshQueue() {
    return queryClient.invalidateQueries({ queryKey: ADMIN_REVIEWS_KEY });
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (isSubmittingRef.current) return;
    setApiError(null);

    const choiceError = validateReasonChoice(action, reason);
    setReasonError(choiceError);
    // Chưa chọn lý do thì dừng tại đây, vì backend chắc chắn trả 400 và request chỉ làm nhân viên chờ.
    if (choiceError !== null) return;

    isSubmittingRef.current = true;
    setIsSubmitting(true);
    try {
      await moderateReview(review.id, { hidden: isHiding, reason });
      await refreshQueue();
      onDone(isHiding ? 'Đã ẩn đánh giá.' : 'Đã hiện lại đánh giá.');
      onClose();
    } catch (error: unknown) {
      // Mở lại cờ gửi để nhân viên chọn lại và gửi lại sau lỗi.
      isSubmittingRef.current = false;
      setIsSubmitting(false);
      if (error instanceof ApiError) {
        const reasonDetail = error.details?.find((detail) => detail.key === 'reason');
        if (reasonDetail) {
          // Lỗi 400 theo trường gắn vào nhóm lý do, đúng chỗ nhân viên cần sửa.
          setReasonError(reasonDetail.message);
          return;
        }
        // Đánh giá đã đổi trạng thái hoặc không còn nữa. Tải lại hàng chờ để dòng hiện đúng trạng thái mới.
        if (error.code === 'REVIEW_STATE_UNCHANGED' || error.code === 'REVIEW_NOT_FOUND') {
          void refreshQueue();
        }
      }
      // Thông báo của server là tiếng Việt và đã nói rõ nguyên nhân, nên hiện nguyên văn.
      setApiError(error instanceof Error ? error.message : 'Cập nhật đánh giá thất bại.');
    }
  }

  return (
    <AdminFormDialog
      isOpen
      onClose={onClose}
      title={isHiding ? 'Ẩn đánh giá' : 'Hiện lại đánh giá'}
      formId="review-moderation-form"
      onSubmit={handleSubmit}
      isSubmitting={isSubmitting}
      submitText={isHiding ? 'Ẩn đánh giá' : 'Hiện lại đánh giá'}
      error={apiError}
    >
      {/* Trích nhận xét dưới dạng văn bản thuần, để nhân viên biết đang thao tác trên đánh giá nào. */}
      <div className="review-excerpt">
        <div className="review-excerpt-head">
          <StarRating rating={review.rating} />
          <span className="muted small">
            {review.authorName}, {review.productName ?? 'Sản phẩm không còn'}
          </span>
        </div>
        <p className="review-excerpt-body">{review.body ?? 'Không có nhận xét.'}</p>
      </div>

      <fieldset
        className="review-reason-group"
        disabled={isSubmitting}
        aria-describedby={reasonError ? `${REASON_FIELD_ID}-error` : undefined}
      >
        <legend>Lý do</legend>
        {reasonCodesFor(action).map((code) => (
          <label key={code} className="check">
            <input
              type="radio"
              name={RADIO_GROUP_NAME}
              value={code}
              checked={reason === code}
              onChange={() => {
                setReason(code);
                // Chọn lại lý do thì lỗi cũ không còn đúng.
                setReasonError(null);
              }}
            />
            {formatReasonLabel(code)}
          </label>
        ))}
        <FieldError inputId={REASON_FIELD_ID} message={reasonError} />
      </fieldset>
    </AdminFormDialog>
  );
}
