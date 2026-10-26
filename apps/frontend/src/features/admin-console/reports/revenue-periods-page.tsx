import { useQuery } from '@tanstack/react-query';
import { ErrorAlert } from '../../../shared/error-alert';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { ReportBarChart, type ReportBarPoint } from './report-bar-chart';
import { formatCompactVnd, formatReportDate, formatVnd } from './report-format';
import { ReportPageFrame } from './report-page-frame';
import { formatBucketLabel, formatBucketTick, REPORT_RANGE_OPTIONS } from './report-period';
import { ReportPeriodControl } from './report-period-controls';
import { fetchSalesReport } from './reports-api';
import { RevenueFunnelPanel } from './revenue-funnel-panel';
import { usePeriodReportParams } from './use-report-params';
import './reports.css';

const PERIOD_EMPTY_MESSAGE = 'Chưa có dữ liệu trong khoảng này';

// Trang doanh thu theo kỳ. Chỉ có một biểu đồ cột và bảng số liệu của nó.
export function RevenuePeriodsReportPage() {
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
    return <ForbiddenNotice permission="reports:read" subject="báo cáo doanh thu theo kỳ" />;
  }

  const buckets = periodQuery.data?.buckets ?? [];
  const points: ReportBarPoint[] = buckets.map((bucket) => ({
    key: bucket.key,
    label: formatBucketLabel(bucket.from, bucket.to, params.granularity),
    tick: formatBucketTick(bucket.from, params.granularity),
    value: bucket.recognizedRevenue,
  }));
  const hasRevenue = points.some((point) => point.value > 0);
  const periodRangeLabel =
    REPORT_RANGE_OPTIONS.find((option) => option.value === params.range)?.label ?? '';
  const periodNote = periodQuery.data
    ? `${periodRangeLabel}, ${formatReportDate(periodQuery.data.from)} - ${formatReportDate(periodQuery.data.to)}, giờ Việt Nam`
    : periodRangeLabel;

  return (
    <ReportPageFrame
      title="Báo cáo doanh thu theo kỳ"
      description="Doanh thu ghi nhận theo ngày thanh toán, tổng hợp theo từng kỳ. Mọi ngày và tháng tính theo giờ Việt Nam."
      controls={
        <ReportPeriodControl
          range={params.range}
          granularity={params.granularity}
          onRangeChange={params.setRange}
          onGranularityChange={params.setGranularity}
        />
      }
    >
      <section className="card report-panel" aria-labelledby="revenue-period-title">
        <div className="card-head report-panel-head">
          <h2 id="revenue-period-title">Doanh thu theo kỳ</h2>
          <span className="report-note muted">{periodNote}</span>
        </div>

        {periodQuery.isLoading && (
          <p className="report-status" role="status">
            Đang tải doanh thu theo kỳ...
          </p>
        )}
        {periodQuery.isError && (
          <ErrorAlert message={periodQuery.error?.message ?? 'Không tải được doanh thu theo kỳ.'} />
        )}
        {periodQuery.isSuccess && !hasRevenue && (
          <p className="empty-state">{PERIOD_EMPTY_MESSAGE}</p>
        )}

        {periodQuery.isSuccess && hasRevenue && (
          <ReportBarChart
            chartLabel={`Doanh thu theo kỳ, ${periodRangeLabel}`}
            points={points}
            formatAxisValue={formatCompactVnd}
            tableCaption={`Bảng doanh thu theo kỳ, ${periodRangeLabel}`}
            tableHead={
              <>
                <th scope="col">Kỳ</th>
                <th scope="col" className="num">
                  Doanh thu
                </th>
              </>
            }
            renderRow={(point) => (
              <tr key={point.key}>
                <th scope="row">{point.label}</th>
                <td className="num">{formatVnd(point.value)}</td>
              </tr>
            )}
          />
        )}
      </section>

      {/* Phễu đọc khoảng ngày của báo cáo doanh thu, nên chỉ dựng khối sau khi có response báo cáo. */}
      {periodQuery.data && (
        <RevenueFunnelPanel from={periodQuery.data.from} to={periodQuery.data.to} />
      )}
    </ReportPageFrame>
  );
}
