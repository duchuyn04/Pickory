import { useQuery } from '@tanstack/react-query';
import { ErrorAlert } from '../../../shared/error-alert';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { ReportBarChart, type ReportBarPoint } from './report-bar-chart';
import { formatCount, formatReportDate } from './report-format';
import { ReportPageFrame } from './report-page-frame';
import { formatBucketLabel, formatBucketTick, REPORT_RANGE_OPTIONS } from './report-period';
import { ReportPeriodControl } from './report-period-controls';
import { fetchSalesReport } from './reports-api';
import { usePeriodReportParams } from './use-report-params';
import './reports.css';

const PERIOD_EMPTY_MESSAGE = 'Chưa có dữ liệu trong khoảng này';

// Trang đơn đã thanh toán theo kỳ. Chỉ có một biểu đồ cột và bảng số liệu của nó.
export function OrdersPeriodsReportPage() {
  const canReadReports = useCan('reports:read');
  const params = usePeriodReportParams();

  // Khóa truy vấn gồm khoảng và độ chia, nên đổi bộ chọn là dữ liệu tự nạp lại.
  const periodQuery = useQuery({
    queryKey: ['admin-sales-report', 'period', params.range, params.granularity],
    queryFn: () => fetchSalesReport({ range: params.range, granularity: params.granularity }),
    enabled: canReadReports,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này. Backend vẫn chặn mọi request thật.
  if (!canReadReports) {
    return (
      <ForbiddenNotice permission="reports:read" subject="báo cáo đơn đã thanh toán theo kỳ" />
    );
  }

  const buckets = periodQuery.data?.buckets ?? [];
  const points: ReportBarPoint[] = buckets.map((bucket) => ({
    key: bucket.key,
    label: formatBucketLabel(bucket.from, bucket.to, params.granularity),
    tick: formatBucketTick(bucket.from, params.granularity),
    value: bucket.paidOrders,
  }));
  const hasOrders = points.some((point) => point.value > 0);
  const periodRangeLabel =
    REPORT_RANGE_OPTIONS.find((option) => option.value === params.range)?.label ?? '';
  // Ghi chú nêu khoảng ngày thật của response, để người đọc đối chiếu với biểu đồ.
  const periodNote = periodQuery.data
    ? `${periodRangeLabel}, ${formatReportDate(periodQuery.data.from)} - ${formatReportDate(periodQuery.data.to)}, giờ Việt Nam`
    : periodRangeLabel;

  return (
    <ReportPageFrame
      title="Báo cáo đơn đã thanh toán theo kỳ"
      description="Số đơn đã thanh toán, tính theo ngày thanh toán và tổng hợp theo từng kỳ. Mọi ngày và tháng tính theo giờ Việt Nam."
      controls={
        <ReportPeriodControl
          range={params.range}
          granularity={params.granularity}
          onRangeChange={params.setRange}
          onGranularityChange={params.setGranularity}
        />
      }
    >
      <section className="card report-panel" aria-labelledby="orders-period-title">
        <div className="card-head report-panel-head">
          <h2 id="orders-period-title">Đơn đã thanh toán theo kỳ</h2>
          <span className="report-note muted">{periodNote}</span>
        </div>

        {periodQuery.isLoading && (
          <p className="report-status" role="status">
            Đang tải đơn đã thanh toán theo kỳ...
          </p>
        )}
        {periodQuery.isError && (
          <ErrorAlert
            message={periodQuery.error?.message ?? 'Không tải được đơn đã thanh toán theo kỳ.'}
          />
        )}
        {periodQuery.isSuccess && !hasOrders && (
          <p className="empty-state">{PERIOD_EMPTY_MESSAGE}</p>
        )}

        {periodQuery.isSuccess && hasOrders && (
          <ReportBarChart
            chartLabel={`Đơn đã thanh toán theo kỳ, ${periodRangeLabel}`}
            points={points}
            formatAxisValue={formatCount}
            tableCaption={`Bảng đơn đã thanh toán theo kỳ, ${periodRangeLabel}`}
            tableHead={
              <>
                <th scope="col">Kỳ</th>
                <th scope="col" className="num">
                  Số đơn
                </th>
              </>
            }
            renderRow={(point) => (
              <tr key={point.key}>
                <th scope="row">{point.label}</th>
                <td className="num">{formatCount(point.value)}</td>
              </tr>
            )}
          />
        )}
      </section>
    </ReportPageFrame>
  );
}
