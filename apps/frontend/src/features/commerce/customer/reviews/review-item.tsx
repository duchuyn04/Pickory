import type { ReactNode } from 'react';
import { formatVietnamDateTime } from '../../../../shared/vietnam-time';
import type { PublicReview } from './reviews-api';
import { StarRating } from './star-rating';

interface ReviewItemProps {
  review: PublicReview;
  // Vùng thao tác dưới nhận xét, ví dụ nút báo cáo.
  children?: ReactNode;
}

// Một đánh giá trong danh sách. Tên và nhận xét là dữ liệu do khách nhập, nên chỉ render bằng text node của React.
export function ReviewItem({ review, children }: ReviewItemProps) {
  return (
    <li className="review-item">
      <div className="review-item-head">
        <strong className="review-author">{review.authorName}</strong>
        <StarRating rating={review.rating} />
        <time className="muted small" dateTime={review.createdAt}>
          {formatVietnamDateTime(review.createdAt)}
        </time>
      </div>
      {/* Không dùng `dangerouslySetInnerHTML` hay parser markdown. CSS `pre-line` giữ các dòng xuống hàng. */}
      {review.body !== null && <p className="review-body">{review.body}</p>}
      {children}
    </li>
  );
}
