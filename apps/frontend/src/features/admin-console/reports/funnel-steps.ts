import { formatPercentValue } from './report-format';
import type { ReportFunnelPoint } from './report-funnel-chart';
import type { FunnelStep } from './reports-api';

// Tên tiếng Việt của ba bước phễu. Mã lạ hiện nguyên mã, để một bước mới không làm mất cả dòng.
const STEP_LABELS: Record<string, string> = {
  product_view: 'Xem sản phẩm',
  add_to_cart: 'Thêm giỏ',
  order_placed: 'Đặt đơn',
};

// Bước đầu không có bước trước. Ô tỉ lệ ghi rõ điều đó thay vì để trống khó hiểu.
const FIRST_STEP_RATE_LABEL = 'Bước đầu';

// Bước trước có 0 lượt thì không có gì để chia. Đây không phải tỉ lệ 0%.
const MISSING_RATE_LABEL = 'Chưa có dữ liệu';

// Một dòng bảng của phễu, kèm tỉ lệ dạng số để bảng và biểu đồ dùng chung một nguồn.
export interface FunnelStepRow extends ReportFunnelPoint {
  rate: number | null;
}

// Đổi các bước API trả thành dòng bảng. Thứ tự giữ theo API, vì API đã xếp theo đường mua hàng.
export function funnelRows(steps: FunnelStep[]): FunnelStepRow[] {
  return steps.map((step, index) => ({
    key: step.metric,
    label: STEP_LABELS[step.metric] ?? step.metric,
    count: step.count,
    rate: step.conversionRate,
    rateLabel: rateLabelForStep(index, step.conversionRate),
  }));
}

// Chữ của ô tỉ lệ. Bước đầu không có tỉ lệ; bước sau có tỉ lệ do API tính từ bước liền trước.
function rateLabelForStep(index: number, rate: number | null): string {
  if (index === 0) {
    return FIRST_STEP_RATE_LABEL;
  }
  if (rate === null) {
    return MISSING_RATE_LABEL;
  }
  return formatPercentValue(rate);
}
