import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ErrorAlert } from '../../../../shared/error-alert';
import { ForbiddenNotice } from '../../../../shared/forbidden-notice';
import { Pager } from '../../../../shared/pager';
import { TabList, tabId } from '../../../../shared/tab-list';
import { useListFilters } from '../../../../shared/use-list-filters';
import { formatVietnamDateTime } from '../../../../shared/vietnam-time';
import { useCan } from '../../../identity/hooks/use-actor';
import { StarRating } from '../../customer/reviews/star-rating';
import {
  REVIEW_FILTER_NAMES,
  fetchAdminReviews,
  normalizeStatusTab,
  type AdminReview,
  type StatusTab,
} from './admin-reviews-api';
import { ADMIN_REVIEWS_KEY, ReviewModerationDialog } from './review-moderation-dialog';
import {
  formatReasonLabel,
  isPriorityReview,
  type ModerationAction,
} from './review-moderation-logic';
import './admin-reviews.css';

const PANEL_ID = 'review-panel';

const STATUS_TABS: { code: StatusTab; label: string }[] = [
  { code: '', label: 'Tất cả' },
  { code: 'visible', label: 'Đang hiện' },
  { code: 'hidden', label: 'Đã ẩn' },
];

const EMPTY_MESSAGES: Record<StatusTab, string> = {
  '': 'Chưa có đánh giá nào.',
  visible: 'Không có đánh giá nào đang hiện.',
  hidden: 'Không có đánh giá nào đã ẩn.',
};

const MISSING_PRODUCT_LABEL = 'Sản phẩm không còn';

interface OpenDialog {
  review: AdminReview;
  action: ModerationAction;
}

// Hàng chờ kiểm duyệt: đánh giá có nhiều báo cáo đứng đầu, nhân viên ẩn hoặc hiện lại kèm lý do.
export function AdminReviewsPage() {
  const canModerate = useCan('reviews:moderate');
  const { values, page, setFilter, setPage } = useListFilters(REVIEW_FILTER_NAMES);
  const [openDialog, setOpenDialog] = useState<OpenDialog | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const selectedTab = normalizeStatusTab(values.status);

  const reviewsQuery = useQuery({
    queryKey: [...ADMIN_REVIEWS_KEY, selectedTab, page],
    queryFn: () => fetchAdminReviews(selectedTab, page),
    // Thiếu quyền thì không gọi API. Backend vẫn tự chặn mọi request thật sự.
    enabled: canModerate,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này.
  if (!canModerate) {
    return <ForbiddenNotice permission="reviews:moderate" subject="hàng chờ kiểm duyệt đánh giá" />;
  }

  const reviews = reviewsQuery.data;

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Kiểm duyệt đánh giá</h1>
          <p>
            Đánh giá bị báo cáo nhiều đứng đầu. Ẩn đánh giá vi phạm hoặc hiện lại đánh giá bị ẩn
            nhầm.
          </p>
        </div>
      </div>

      {notice && (
        <p role="status" className="alert alert-ok">
          {notice}
        </p>
      )}

      <section className="card card-flush">
        <TabList
          label="Trạng thái đánh giá"
          idPrefix="review"
          panelId={PANEL_ID}
          tabs={STATUS_TABS}
          selected={selectedTab}
          onSelect={(code) => setFilter('status', code)}
        />

        <div id={PANEL_ID} role="tabpanel" aria-labelledby={tabId('review', selectedTab)}>
          {reviewsQuery.isLoading ? (
            <p role="status" className="empty-state">
              Đang nạp danh sách đánh giá...
            </p>
          ) : reviewsQuery.isError ? (
            <div className="review-list-alert">
              <ErrorAlert
                message={
                  reviewsQuery.error instanceof Error
                    ? reviewsQuery.error.message
                    : 'Tải danh sách đánh giá thất bại.'
                }
              />
              <button
                type="button"
                className="btn btn-sm"
                onClick={() => void reviewsQuery.refetch()}
              >
                Thử lại
              </button>
            </div>
          ) : reviews && reviews.items.length === 0 ? (
            <p className="empty-state">{EMPTY_MESSAGES[selectedTab]}</p>
          ) : (
            <div className="table-wrap">
              <table className="tbl review-table">
                <thead>
                  <tr>
                    <th scope="col">Sản phẩm</th>
                    <th scope="col">Người viết</th>
                    <th scope="col">Đánh giá</th>
                    <th scope="col" className="num">
                      Báo cáo
                    </th>
                    <th scope="col">Trạng thái</th>
                    <th scope="col" className="num">
                      Thao tác
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {reviews?.items.map((review) => (
                    <tr key={review.id}>
                      <th scope="row" data-label="Sản phẩm" className="wrap-anywhere">
                        <div className="review-cell">
                          {review.productName ?? MISSING_PRODUCT_LABEL}
                        </div>
                      </th>
                      <td data-label="Người viết">
                        <div className="review-cell">
                          <span className="wrap-anywhere">{review.authorName}</span>
                          <span className="cell-sub">
                            {formatVietnamDateTime(review.createdAt)}
                          </span>
                        </div>
                      </td>
                      <td data-label="Đánh giá" className="review-cell-body">
                        <div className="review-cell">
                          <StarRating rating={review.rating} />
                          {/* Nhận xét do khách nhập, nên chỉ render bằng text node của React. */}
                          {review.body !== null && <p className="review-text">{review.body}</p>}
                        </div>
                      </td>
                      <td data-label="Báo cáo" className="num">
                        <div className="review-cell">
                          {review.reportCount}
                          {isPriorityReview(review.reportCount) && (
                            <span className="badge badge-bad review-priority">Ưu tiên</span>
                          )}
                        </div>
                      </td>
                      <td data-label="Trạng thái">
                        <div className="review-cell">
                          {review.hidden ? (
                            <>
                              <span className="badge">Đã ẩn</span>
                              <span className="cell-sub">
                                {formatReasonLabel(review.hiddenReason)}
                              </span>
                              {review.hiddenAt !== null && (
                                <span className="cell-sub">
                                  {formatVietnamDateTime(review.hiddenAt)}
                                </span>
                              )}
                            </>
                          ) : (
                            <span className="badge badge-ok">Đang hiện</span>
                          )}
                        </div>
                      </td>
                      <td data-label="Thao tác" className="num">
                        <div className="review-cell">
                          <button
                            type="button"
                            className={review.hidden ? 'btn btn-sm' : 'btn btn-sm btn-danger'}
                            // Tên truy cập kèm người viết và sản phẩm, để trình đọc màn hình phân biệt các nút.
                            aria-label={`${review.hidden ? 'Hiện lại' : 'Ẩn'} đánh giá của ${review.authorName} về ${review.productName ?? MISSING_PRODUCT_LABEL}`}
                            onClick={() =>
                              setOpenDialog({ review, action: review.hidden ? 'show' : 'hide' })
                            }
                          >
                            {review.hidden ? 'Hiện lại' : 'Ẩn'}
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {reviews && !reviewsQuery.isError && (
          <Pager
            page={page}
            total={reviews.total}
            limit={reviews.limit}
            onPageChange={setPage}
            label="Phân trang đánh giá"
          />
        )}
      </section>

      {openDialog !== null && (
        <ReviewModerationDialog
          review={openDialog.review}
          action={openDialog.action}
          onClose={() => setOpenDialog(null)}
          onDone={setNotice}
        />
      )}
    </div>
  );
}
