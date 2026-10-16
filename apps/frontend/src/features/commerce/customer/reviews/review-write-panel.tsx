import { useState } from 'react';
import { Link } from 'react-router-dom';
import { formatVietnamDateTime } from '../../../../shared/vietnam-time';
import { ReviewFormCard } from './review-form-card';
import type { WriteState } from './review-write-state';
import { StarRating } from './star-rating';

interface ReviewWritePanelProps {
  productId: string;
  state: WriteState;
}

// Khối viết đánh giá. Người chưa đủ điều kiện thấy lý do thay cho form.
export function ReviewWritePanel({ productId, state }: ReviewWritePanelProps) {
  const [isEditing, setIsEditing] = useState(false);

  switch (state.kind) {
    case 'loading':
      return null;
    case 'sign-in':
      return (
        <p className="review-write-note">
          {/* Trang đăng nhập chưa nhận đường quay lại, nên link chỉ đưa tới `/login`. */}
          <Link to="/login">Đăng nhập để viết đánh giá</Link>
        </p>
      );
    case 'staff':
      return <p className="review-write-note">Tài khoản nhân viên không viết đánh giá.</p>;
    case 'not-purchased':
      return (
        <p className="review-write-note">Chỉ khách đã nhận sản phẩm này mới viết được đánh giá.</p>
      );
    case 'form':
      return (
        <div className="review-write">
          <h3>Viết đánh giá của bạn</h3>
          <ReviewFormCard productId={productId} editing={null} onDone={() => undefined} />
        </div>
      );
    case 'own-review':
      return (
        <div className="review-write">
          <h3>Đánh giá của bạn</h3>
          {isEditing ? (
            <ReviewFormCard
              productId={productId}
              editing={state.review}
              onDone={() => setIsEditing(false)}
              onCancel={() => setIsEditing(false)}
            />
          ) : (
            <div className="review-own stack-sm">
              <div className="review-item-head">
                <StarRating rating={state.review.rating} />
                <time className="muted small" dateTime={state.review.createdAt}>
                  {formatVietnamDateTime(state.review.createdAt)}
                </time>
              </div>
              {state.review.body !== null && <p className="review-body">{state.review.body}</p>}
              {state.review.hidden && (
                <p className="review-hidden-note">Đánh giá của bạn đang bị ẩn.</p>
              )}
              {state.canEdit ? (
                <div>
                  <button type="button" className="btn btn-sm" onClick={() => setIsEditing(true)}>
                    Sửa đánh giá
                  </button>
                </div>
              ) : (
                <p className="muted small">Đã quá 30 ngày, không sửa được đánh giá.</p>
              )}
            </div>
          )}
        </div>
      );
  }
}
