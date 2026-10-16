import { useState } from 'react';
import { ApiError } from '../../../../shared/api';
import { ErrorAlert } from '../../../../shared/error-alert';
import { useActor } from '../../../identity/hooks/use-actor';
import { ReportReviewDialog } from './report-review-dialog';
import { ReviewItem } from './review-item';
import { ReviewSummaryPanel } from './review-summary-panel';
import { ReviewWritePanel } from './review-write-panel';
import { chooseWriteState } from './review-write-state';
import { reportReview, useMyReview, useProductReviews, type PublicReview } from './reviews-api';
import './reviews.css';

const ALREADY_REPORTED_CODE = 'REVIEW_ALREADY_REPORTED';

interface ReviewsSectionProps {
  productId: string;
  // Hàm lấy giờ hiện tại. Test truyền giờ cố định để hạn sửa không phụ thuộc đồng hồ máy.
  getNow?: () => Date;
}

function currentTime(): Date {
  return new Date();
}

// Khối đánh giá trên trang sản phẩm. Lỗi của khối này chỉ hiện trong khối, không ảnh hưởng phần mua hàng.
export function ReviewsSection({ productId, getNow = currentTime }: ReviewsSectionProps) {
  const { data: actor } = useActor();
  // Chỉ khách đã đăng nhập mới có `/mine`. Khách vãng lai nhận 401 và nhân viên nhận 403.
  const isCustomer = actor?.kind === 'customer';
  const reviewsQuery = useProductReviews(productId);
  const mineQuery = useMyReview(productId, isCustomer);
  const [reportingReview, setReportingReview] = useState<PublicReview | null>(null);
  const [reportedIds, setReportedIds] = useState<ReadonlySet<string>>(new Set());

  const writeState = chooseWriteState({ actor, mine: mineQuery.data, now: getNow() });
  const ownReviewId = mineQuery.data?.review?.id ?? null;
  const pages = reviewsQuery.data?.pages ?? [];
  const summary = pages[0]?.summary;
  const reviews = pages.flatMap((page) => page.items);
  // Nút báo cáo cần biết đâu là đánh giá của chính khách, nên chỉ hiện sau khi `/mine` đã về.
  const canReport = isCustomer && mineQuery.data !== undefined;

  async function confirmReport(review: PublicReview) {
    try {
      await reportReview(review.id);
    } catch (error: unknown) {
      // Khách đã báo cáo đánh giá này ở lần trước. Kết quả giống như vừa báo cáo xong.
      if (!(error instanceof ApiError && error.code === ALREADY_REPORTED_CODE)) throw error;
    }
    setReportedIds((current) => new Set(current).add(review.id));
    setReportingReview(null);
  }

  return (
    <section className="product-info-section reviews" aria-labelledby="product-reviews-title">
      <h2 id="product-reviews-title" className="product-info-title">
        Đánh giá sản phẩm
      </h2>

      {reviewsQuery.isPending ? (
        <p role="status" className="muted">
          Đang tải đánh giá...
        </p>
      ) : summary === undefined ? (
        // Chưa có trang đầu nghĩa là lần tải đầu lỗi. Lỗi tải trang sau không vào nhánh này, để tóm tắt vẫn hiện.
        <div className="stack-sm">
          <ErrorAlert
            message={
              reviewsQuery.error instanceof Error
                ? reviewsQuery.error.message
                : 'Không tải được đánh giá.'
            }
          />
          <div>
            <button
              type="button"
              className="btn btn-sm"
              onClick={() => void reviewsQuery.refetch()}
            >
              Thử lại
            </button>
          </div>
        </div>
      ) : (
        <ReviewSummaryPanel summary={summary} />
      )}

      {mineQuery.isError ? (
        <div className="stack-sm review-write-note">
          <ErrorAlert message="Không kiểm tra được quyền viết đánh giá của bạn." />
          <div>
            <button type="button" className="btn btn-sm" onClick={() => void mineQuery.refetch()}>
              Thử lại
            </button>
          </div>
        </div>
      ) : (
        <ReviewWritePanel productId={productId} state={writeState} />
      )}

      {reviews.length > 0 && (
        <ul className="review-list">
          {reviews.map((review) => (
            <ReviewItem key={review.id} review={review}>
              {canReport && review.id !== ownReviewId && (
                <div className="review-item-actions">
                  <button
                    type="button"
                    className="btn btn-sm btn-ghost"
                    disabled={reportedIds.has(review.id)}
                    onClick={() => setReportingReview(review)}
                  >
                    {reportedIds.has(review.id) ? 'Đã báo cáo' : 'Báo cáo'}
                  </button>
                </div>
              )}
            </ReviewItem>
          ))}
        </ul>
      )}

      {reviewsQuery.hasNextPage && (
        <div className="review-more stack-sm">
          {/* Lỗi tải trang sau hiện cạnh nút, vì tóm tắt và các dòng đã tải vẫn còn dùng được. */}
          {reviewsQuery.isFetchNextPageError && (
            <ErrorAlert
              message={
                reviewsQuery.error instanceof Error
                  ? reviewsQuery.error.message
                  : 'Không tải được thêm đánh giá.'
              }
            />
          )}
          <div>
            <button
              type="button"
              className="btn"
              disabled={reviewsQuery.isFetchingNextPage}
              onClick={() => void reviewsQuery.fetchNextPage()}
            >
              {reviewsQuery.isFetchingNextPage ? 'Đang tải...' : 'Xem thêm đánh giá'}
            </button>
          </div>
        </div>
      )}

      {reportingReview !== null && (
        <ReportReviewDialog
          onConfirm={() => confirmReport(reportingReview)}
          onClose={() => setReportingReview(null)}
        />
      )}
    </section>
  );
}
