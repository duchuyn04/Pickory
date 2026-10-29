import { REPORT_OTHER_COLOR, REPORT_SERIES_COLORS, reportSeriesColor } from './report-colors';
import type { ReportPieSlice } from './report-pie-chart';
import type {
  SalesReportCancellations,
  SalesReportPaymentMethods,
  SalesReportTopProduct,
} from './reports-api';

export type PaymentMethodGroupKey = 'cod' | 'sepay';
export type CancellationReasonKey = 'customer' | 'shop' | 'payment_expired';

// Một nhóm của biểu đồ tròn. `key` cố định để dashboard và trang chi tiết cùng nhóm cùng màu.
export interface ReportGroup<Key extends string = string> {
  key: Key;
  label: string;
  color: string;
}

// Hai phương thức thanh toán của cửa hàng. Mỗi phương thức giữ một màu ở mọi biểu đồ.
export const PAYMENT_METHOD_GROUPS = [
  { key: 'cod', label: 'COD', color: REPORT_SERIES_COLORS[0] },
  { key: 'sepay', label: 'SePay', color: REPORT_SERIES_COLORS[1] },
] as const satisfies readonly ReportGroup<PaymentMethodGroupKey>[];

// Ba lý do hủy mà báo cáo đếm riêng.
export const CANCELLATION_REASON_GROUPS = [
  { key: 'customer', label: 'Khách hủy', color: REPORT_SERIES_COLORS[0] },
  { key: 'shop', label: 'Shop hủy', color: REPORT_SERIES_COLORS[1] },
  { key: 'payment_expired', label: 'Quá hạn thanh toán', color: REPORT_SERIES_COLORS[2] },
] as const satisfies readonly ReportGroup<CancellationReasonKey>[];

export const OTHER_PRODUCT_KEY = 'other';
export const OTHER_PRODUCT_LABEL = 'Khác';

// Số sản phẩm hiện riêng trên biểu đồ tròn. Phần còn lại vào nhóm "Khác".
export const MAX_PRODUCT_SLICES = 5;

const UNKNOWN_PRODUCT_NAME = 'Không rõ tên sản phẩm';
const UNKNOWN_BRAND_NAME = 'Không rõ thương hiệu';

// Dựng lát từ bộ đếm của từng nhóm theo đúng thứ tự nhóm. Nhóm 0 đơn không có lát để vẽ hay nhấn.
function slicesFromCounts<Key extends string>(
  groups: readonly ReportGroup<Key>[],
  counts: readonly number[],
): ReportPieSlice[] {
  const slices: ReportPieSlice[] = [];
  groups.forEach((group, index) => {
    const value = counts[index] ?? 0;
    if (value > 0) {
      slices.push({ key: group.key, label: group.label, value, fill: group.color });
    }
  });
  return slices;
}

// Biểu đồ tròn của số đơn đã thanh toán theo phương thức.
export function buildPaymentMethodSlices(counts: SalesReportPaymentMethods): ReportPieSlice[] {
  return slicesFromCounts(PAYMENT_METHOD_GROUPS, [counts.cod, counts.sepay]);
}

// Biểu đồ tròn của số đơn hủy theo lý do.
export function buildCancellationSlices(counts: SalesReportCancellations): ReportPieSlice[] {
  return slicesFromCounts(CANCELLATION_REASON_GROUPS, [
    counts.byCustomer,
    counts.byShop,
    counts.paymentExpired,
  ]);
}

// Số đơn đã thanh toán của một phương thức trong bộ đếm của một ngày hoặc một tháng.
export function countPaymentMethodOrders(
  key: PaymentMethodGroupKey,
  counts: SalesReportPaymentMethods,
): number {
  return key === 'sepay' ? counts.sepay : counts.cod;
}

// Số đơn hủy của một lý do trong bộ đếm của một ngày hoặc một tháng.
export function countCancelledOrders(
  key: CancellationReasonKey,
  counts: SalesReportCancellations,
): number {
  // Mỗi mã nhóm trả đúng bộ đếm của nó, để bảng chi tiết không đọc lệch trường.
  if (key === 'shop') return counts.byShop;
  if (key === 'payment_expired') return counts.paymentExpired;
  return counts.byCustomer;
}

// Tổng số đơn hủy của một kỳ, dùng cho biểu đồ cột và tổng của tháng.
export function totalCancelledOrders(counts: SalesReportCancellations): number {
  return counts.byCustomer + counts.byShop + counts.paymentExpired;
}

export interface ProductGroupSplit {
  // Sản phẩm hiện riêng trên biểu đồ tròn, bán nhiều nhất trước.
  top: SalesReportTopProduct[];
  // Sản phẩm gộp vào nhóm "Khác", cùng thứ tự với `top`.
  rest: SalesReportTopProduct[];
}

// Sắp sản phẩm bán nhiều nhất trước rồi tách nhóm "Khác". Số lượng bằng nhau thì doanh thu cao trước, rồi tên.
export function splitTopProducts(products: readonly SalesReportTopProduct[]): ProductGroupSplit {
  const sorted = [...products].sort((left, right) => {
    if (right.quantity !== left.quantity) return right.quantity - left.quantity;
    if (right.revenue !== left.revenue) return right.revenue - left.revenue;
    const byName = (left.name ?? '').localeCompare(right.name ?? '', 'vi');
    if (byName !== 0) return byName;
    return left.productId.localeCompare(right.productId);
  });
  return { top: sorted.slice(0, MAX_PRODUCT_SLICES), rest: sorted.slice(MAX_PRODUCT_SLICES) };
}

// Dựng lát của biểu đồ tròn sản phẩm. Nhóm "Khác" cộng số lượng của các sản phẩm còn lại.
export function buildProductSlices(split: ProductGroupSplit): ReportPieSlice[] {
  const slices: ReportPieSlice[] = split.top.map((item, index) => ({
    key: item.productId,
    label: productLabel(item),
    value: item.quantity,
    fill: reportSeriesColor(index),
  }));

  const otherQuantity = split.rest.reduce((sum, item) => sum + item.quantity, 0);
  if (otherQuantity > 0) {
    slices.push({
      key: OTHER_PRODUCT_KEY,
      label: OTHER_PRODUCT_LABEL,
      value: otherQuantity,
      // Nhóm "Khác" dùng màu trung tính để không tranh với sản phẩm thật.
      fill: REPORT_OTHER_COLOR,
    });
  }
  return slices;
}

// Tên sản phẩm hiện trên biểu đồ và bảng. Sản phẩm đã xóa vẫn cần nhãn đọc được.
export function productLabel(product: SalesReportTopProduct): string {
  return product.name?.trim() || UNKNOWN_PRODUCT_NAME;
}

// Tên thương hiệu hiện trong bảng chi tiết. Sản phẩm không có thương hiệu vẫn cần nhãn đọc được.
export function brandLabel(product: SalesReportTopProduct): string {
  return product.brandName?.trim() || UNKNOWN_BRAND_NAME;
}
