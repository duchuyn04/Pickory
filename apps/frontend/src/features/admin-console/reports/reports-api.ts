import { apiRequest } from '../../../shared/api';
import type { ReportGranularityCode, ReportRangeCode } from './report-period';

// Số đơn đã thanh toán theo phương thức trong một kỳ. Báo cáo chỉ có hai phương thức của cửa hàng.
export interface SalesReportPaymentMethods {
  cod: number;
  sepay: number;
}

// Số đơn hủy theo lý do trong một kỳ.
export interface SalesReportCancellations {
  byCustomer: number;
  byShop: number;
  paymentExpired: number;
}

// Một nhóm thời gian của biểu đồ cột. Ngày là ngày lịch Việt Nam, gồm cả hai đầu.
export interface SalesReportBucket {
  key: string;
  from: string;
  to: string;
  // Doanh thu ghi nhận của nhóm, tính bằng VND.
  recognizedRevenue: number;
  paidOrders: number;
  paidByMethod: SalesReportPaymentMethods;
  cancellations: SalesReportCancellations;
  soldQuantity: number;
}

// Doanh thu gộp theo thương hiệu trong tháng. API trả thêm số lượng và sản phẩm, nhưng trang chỉ đọc ba trường này.
export interface SalesReportBrand {
  // `null` với sản phẩm catalog không trả về. Báo cáo gộp nhóm này vào lát "Khác".
  brandId: string | null;
  brandName: string | null;
  revenue: number;
}

// Doanh thu và số lượng của một sản phẩm bán ra trong tháng.
export interface SalesReportTopProduct {
  productId: string;
  // `null` khi catalog không trả về sản phẩm, ví dụ sản phẩm đã bị xóa.
  name: string | null;
  brandName: string | null;
  revenue: number;
  quantity: number;
}

// Tổng hợp toàn bộ khoảng báo cáo. Trang biểu đồ tròn đọc các chỉ số của tháng đang chọn từ đây.
export interface SalesReportTotals {
  recognizedRevenue: number;
  paidOrders: number;
  soldQuantity: number;
  paidByMethod: SalesReportPaymentMethods;
  cancellations: SalesReportCancellations;
}

// Phần dữ liệu các trang báo cáo dùng từ response báo cáo. Các chỉ số khác của API chưa cần ở đây.
export interface SalesReportData {
  from: string;
  to: string;
  buckets: SalesReportBucket[];
  brands: SalesReportBrand[];
  totals: SalesReportTotals;
  topProducts: SalesReportTopProduct[];
}

// `month` là khoảng của trang biểu đồ tròn, không nằm trong bộ chọn khoảng của trang theo kỳ.
export type SalesReportRange = ReportRangeCode | 'month';

export interface SalesReportQuery {
  range: SalesReportRange;
  granularity: ReportGranularityCode;
  // Bắt buộc khi `range` là `month`. Các khoảng khác không có tham số này.
  month?: string;
}

// Gọi route báo cáo doanh thu của ADM-12. Mỗi trang gọi một lần với đúng khoảng của nó.
export function fetchSalesReport(query: SalesReportQuery): Promise<SalesReportData> {
  const params = new URLSearchParams({ range: query.range, granularity: query.granularity });
  // Chỉ gửi `month` khi khoảng là một tháng, vì API từ chối tham số thừa ở khoảng khác.
  if (query.range === 'month' && query.month) params.set('month', query.month);
  return apiRequest<SalesReportData>(`/api/admin/reports/sales?${params.toString()}`);
}

// Một bước của phễu. `conversionRate` là tỉ lệ so với bước trước, từ 0 đến 1.
// API trả `null` khi bước trước không có lượt, hoặc ở bước đầu vì không có bước trước.
export interface FunnelStep {
  // Mã chỉ số của activity-tracking, ví dụ `product_view`.
  metric: string;
  count: number;
  conversionRate: number | null;
}

// Response của route phễu của ACT-11. `from` và `to` là ngày lịch Việt Nam, gồm cả hai đầu.
export interface FunnelReportData {
  from: string;
  to: string;
  steps: FunnelStep[];
}

// Gọi route phễu với khoảng ngày của báo cáo doanh thu. Hai route dùng chung một khoảng để số liệu khớp nhau.
export function fetchFunnelReport(range: { from: string; to: string }): Promise<FunnelReportData> {
  const params = new URLSearchParams({ from: range.from, to: range.to });
  return apiRequest<FunnelReportData>(`/api/admin/analytics/funnel?${params.toString()}`);
}
