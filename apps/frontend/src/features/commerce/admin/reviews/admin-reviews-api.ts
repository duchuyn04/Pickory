import { apiRequest } from '../../../../shared/api';

// Tên tham số trên URL của hàng chờ. `status` chọn tab trạng thái.
export const REVIEW_FILTER_NAMES = ['status'] as const;

// Tab trạng thái. Chuỗi rỗng là tab "Tất cả", và hai giá trị còn lại là giá trị `status` của API.
export type StatusTab = '' | 'visible' | 'hidden';

// Một dòng của hàng chờ. Backend không trả `userId`, email hay số điện thoại của người viết.
export interface AdminReview {
  id: string;
  productId: string;
  // `null` khi catalog không còn sản phẩm đó.
  productName: string | null;
  rating: number;
  body: string | null;
  authorName: string;
  reportCount: number;
  hidden: boolean;
  hiddenReason: string | null;
  hiddenAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface AdminReviewList {
  items: AdminReview[];
  total: number;
  page: number;
  limit: number;
}

// Giá trị `status` lạ trên URL về tab "Tất cả", để link sai không làm API trả 400.
export function normalizeStatusTab(raw: string): StatusTab {
  return raw === 'visible' || raw === 'hidden' ? raw : '';
}

// Dựng đường dẫn hàng chờ. Chỉ gửi tham số có giá trị, và bỏ `page` khi là trang 1.
export function buildAdminReviewsPath(status: StatusTab, page: number): string {
  const params = new URLSearchParams();
  if (status !== '') params.set('status', status);
  if (page > 1) params.set('page', String(page));
  const queryString = params.toString();
  return queryString ? `/api/admin/reviews?${queryString}` : '/api/admin/reviews';
}

export function fetchAdminReviews(status: StatusTab, page: number) {
  return apiRequest<AdminReviewList>(buildAdminReviewsPath(status, page));
}

// Ẩn hoặc hiện lại một đánh giá. `reason` là mã trong danh sách của hành động.
export function moderateReview(id: string, body: { hidden: boolean; reason: string }) {
  return apiRequest<AdminReview>(`/api/admin/reviews/${encodeURIComponent(id)}`, {
    method: 'PATCH',
    body: JSON.stringify(body),
  });
}
