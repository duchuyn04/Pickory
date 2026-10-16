import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type FormEvent } from 'react';
import { ApiError } from '../../../../shared/api';
import { ErrorAlert } from '../../../../shared/error-alert';
import { FieldError, fieldErrorProps } from '../../../../shared/field-error';
import { RatingPicker } from './rating-picker';
import {
  REVIEW_BODY_MAX_LENGTH,
  countReviewBodyCharacters,
  mapReviewServerErrors,
  toReviewPayload,
  validateReviewForm,
  type ReviewFieldErrors,
} from './review-form';
import {
  createReview,
  myReviewKey,
  productReviewsKey,
  updateReview,
  type OwnReview,
} from './reviews-api';

const BODY_FIELD_ID = 'review-body';
const BODY_COUNTER_ID = 'review-body-count';

interface ReviewFormCardProps {
  productId: string;
  // Đánh giá đang sửa. `null` là viết đánh giá mới.
  editing: OwnReview | null;
  // Gọi sau khi gửi thành công và danh sách đã tải lại.
  onDone: () => void;
  // Chỉ có khi sửa, để khách bỏ sửa mà không gửi gì.
  onCancel?: () => void;
}

// Form viết hoặc sửa đánh giá. Lỗi theo trường của API hiện dưới đúng trường.
export function ReviewFormCard({ productId, editing, onDone, onCancel }: ReviewFormCardProps) {
  const queryClient = useQueryClient();
  const [rating, setRating] = useState<number | null>(editing?.rating ?? null);
  const [body, setBody] = useState(editing?.body ?? '');
  const [fieldErrors, setFieldErrors] = useState<ReviewFieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [ratingFocusSignal, setRatingFocusSignal] = useState(0);
  // Cờ đồng bộ chặn bấm đúp. State `isSubmitting` chỉ cập nhật sau lần render kế tiếp.
  const isSubmittingRef = useRef(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (isSubmittingRef.current) return;
    setApiError(null);

    const errors = validateReviewForm({ rating, body });
    setFieldErrors(errors);
    // Form sai dừng tại đây, để không gửi request chắc chắn bị backend từ chối.
    if (Object.keys(errors).length > 0) {
      // Thiếu sao thì đưa focus về nhóm sao, vì đó là chỗ cần sửa đầu tiên.
      if (errors.rating) setRatingFocusSignal((current) => current + 1);
      return;
    }
    // Form đã qua kiểm tra nên số sao chắc chắn có giá trị. Nhánh này chỉ làm TypeScript hiểu điều đó.
    if (rating === null) return;

    isSubmittingRef.current = true;
    setIsSubmitting(true);
    try {
      const payload = toReviewPayload({ rating, body });
      if (editing) {
        await updateReview(productId, payload);
      } else {
        await createReview(productId, payload);
      }
      // Tải lại cả danh sách và trạng thái của khách, để điểm trung bình và khối viết đổi ngay.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: productReviewsKey(productId) }),
        queryClient.invalidateQueries({ queryKey: myReviewKey(productId) }),
      ]);
      onDone();
    } catch (error: unknown) {
      // Mở lại cờ gửi để khách sửa form rồi gửi lại sau lỗi.
      isSubmittingRef.current = false;
      setIsSubmitting(false);
      if (error instanceof ApiError && error.details && error.details.length > 0) {
        const { fieldErrors: serverErrors, unmatched } = mapReviewServerErrors(error.details);
        setFieldErrors(serverErrors);
        // Lỗi không gắn được vào trường nào thì hiện chung, để khách không mất thông tin.
        setApiError(unmatched.length > 0 ? unmatched.join(' ') : null);
        return;
      }
      // Lỗi 403, 409 và lỗi mạng đã có thông báo tiếng Việt từ `apiRequest`, nên hiện nguyên văn.
      setApiError(error instanceof Error ? error.message : 'Gửi đánh giá thất bại.');
    }
  }

  const bodyDescribedBy = fieldErrors.body
    ? `${BODY_COUNTER_ID} ${BODY_FIELD_ID}-error`
    : BODY_COUNTER_ID;
  const bodyLength = countReviewBodyCharacters(body);
  const isBodyTooLong = bodyLength > REVIEW_BODY_MAX_LENGTH;

  return (
    <form className="review-form stack" noValidate onSubmit={handleSubmit}>
      {apiError && <ErrorAlert message={apiError} />}

      <RatingPicker
        value={rating}
        onChange={(next) => {
          setRating(next);
          // Chọn lại sao thì lỗi cũ không còn đúng.
          setFieldErrors((current) => ({ ...current, rating: null }));
        }}
        error={fieldErrors.rating}
        focusSignal={ratingFocusSignal}
        isDisabled={isSubmitting}
      />

      <div className="field">
        <label htmlFor={BODY_FIELD_ID}>
          Nhận xét <small>(không bắt buộc)</small>
        </label>
        <textarea
          id={BODY_FIELD_ID}
          rows={4}
          value={body}
          disabled={isSubmitting}
          onChange={(event) => {
            setBody(event.target.value);
            setFieldErrors((current) => ({ ...current, body: null }));
          }}
          {...fieldErrorProps(BODY_FIELD_ID, fieldErrors.body)}
          aria-describedby={bodyDescribedBy}
        />
        <span
          id={BODY_COUNTER_ID}
          className={isBodyTooLong ? 'review-counter review-counter-over' : 'review-counter'}
        >
          {bodyLength.toLocaleString('vi-VN')}/{REVIEW_BODY_MAX_LENGTH.toLocaleString('vi-VN')}
        </span>
        <FieldError inputId={BODY_FIELD_ID} message={fieldErrors.body} />
      </div>

      <div className="review-form-actions">
        <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
          {isSubmitting ? 'Đang gửi...' : editing ? 'Lưu đánh giá' : 'Gửi đánh giá'}
        </button>
        {onCancel && (
          <button type="button" className="btn" disabled={isSubmitting} onClick={onCancel}>
            Hủy
          </button>
        )}
      </div>
    </form>
  );
}
