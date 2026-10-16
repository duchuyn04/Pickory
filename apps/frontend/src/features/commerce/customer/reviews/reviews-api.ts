import { useInfiniteQuery, useQuery, type InfiniteData } from '@tanstack/react-query';
import { apiRequest, type ApiError } from '../../../../shared/api';

// Số đánh giá mỗi trang. Nằm trong khoảng 1 đến 50 mà backend cho phép.
export const REVIEW_PAGE_SIZE = 10;

// Khóa truy vấn của danh sách và tóm tắt đánh giá. Sau khi gửi hoặc sửa thì đánh dấu cũ khóa này.
export function productReviewsKey(productId: string) {
  return ['product-reviews', productId] as const;
}

// Khóa truy vấn trạng thái viết đánh giá của chính khách.
export function myReviewKey(productId: string) {
  return ['product-review-mine', productId] as const;
}

// Tóm tắt các đánh giá đang hiện. `average` là `null` khi chưa có đánh giá.
export interface ReviewSummary {
  average: number | null;
  count: number;
  // Khóa "1" đến "5", mỗi giá trị là số đánh giá của mức sao đó.
  distribution: Record<string, number>;
}

// Một đánh giá công khai. Backend không trả email hay mã người viết.
export interface PublicReview {
  id: string;
  rating: number;
  body: string | null;
  authorName: string;
  createdAt: string;
  updatedAt: string;
}

export interface ReviewPage {
  summary: ReviewSummary;
  items: PublicReview[];
  nextCursor: string | null;
}

// Đánh giá của chính khách. `hidden` báo đánh giá đang bị ẩn, `editableUntil` là hạn sửa.
export interface OwnReview {
  id: string;
  rating: number;
  body: string | null;
  hidden: boolean;
  createdAt: string;
  updatedAt: string;
  editableUntil: string;
}

// Lý do khách chưa viết được đánh giá. Backend chỉ trả hai mã này.
export type ReviewIneligibleReason = 'NOT_PURCHASED' | 'ALREADY_REVIEWED';

export interface MyReviewStatus {
  eligible: boolean;
  reason: ReviewIneligibleReason | null;
  review: OwnReview | null;
}

// Body gửi lên khi viết hoặc sửa đánh giá.
export interface ReviewPayload {
  rating: number;
  body: string | null;
}

export function fetchReviewPage(productId: string, cursor: string | null): Promise<ReviewPage> {
  const query = new URLSearchParams({ limit: String(REVIEW_PAGE_SIZE) });
  if (cursor !== null) query.set('cursor', cursor);
  return apiRequest<ReviewPage>(
    `/api/products/${encodeURIComponent(productId)}/reviews?${query.toString()}`,
  );
}

export function fetchMyReview(productId: string): Promise<MyReviewStatus> {
  return apiRequest<MyReviewStatus>(`/api/products/${encodeURIComponent(productId)}/reviews/mine`);
}

export function createReview(productId: string, payload: ReviewPayload): Promise<OwnReview> {
  return apiRequest<OwnReview>(`/api/products/${encodeURIComponent(productId)}/reviews`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export function updateReview(productId: string, payload: ReviewPayload): Promise<OwnReview> {
  return apiRequest<OwnReview>(`/api/products/${encodeURIComponent(productId)}/reviews/mine`, {
    method: 'PATCH',
    body: JSON.stringify(payload),
  });
}

export function reportReview(reviewId: string): Promise<{ reviewId: string; createdAt: string }> {
  return apiRequest(`/api/reviews/${encodeURIComponent(reviewId)}/reports`, { method: 'POST' });
}

// Danh sách công khai theo cursor. Mỗi lần gọi `fetchNextPage` nối thêm một trang vào cuối.
export function useProductReviews(productId: string) {
  return useInfiniteQuery<
    ReviewPage,
    ApiError,
    InfiniteData<ReviewPage, string | null>,
    readonly unknown[],
    string | null
  >({
    queryKey: productReviewsKey(productId),
    queryFn: ({ pageParam }) => fetchReviewPage(productId, pageParam),
    initialPageParam: null,
    getNextPageParam: (lastPage) => lastPage.nextCursor,
  });
}

// Trạng thái viết đánh giá. Chỉ khách đã đăng nhập mới gọi, vì khách vãng lai nhận 401 và nhân viên nhận 403.
export function useMyReview(productId: string, isEnabled: boolean) {
  return useQuery<MyReviewStatus, ApiError>({
    queryKey: myReviewKey(productId),
    queryFn: () => fetchMyReview(productId),
    enabled: isEnabled,
  });
}
