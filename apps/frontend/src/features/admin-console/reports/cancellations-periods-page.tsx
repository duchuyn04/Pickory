import { useQuery } from '@tanstack/react-query';
import { ErrorAlert } from '../../../shared/error-alert';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { ReportBarChart, type ReportBarPoint } from './report-bar-chart';
import { formatCount, formatReportDate } from './report-format';
import { totalCancelledOrders } from './report-groups';
import { ReportPageFrame } from './report-page-frame';
import { formatBucketLabel, formatBucketTick, REPORT_RANGE_OPTIONS } from './report-period';
import { ReportPeriodControl } from './report-period-controls';
import { fetchSalesReport } from './reports-api';
import { usePeriodReportParams } from './use-report-params';
import './reports.css';

const PERIOD_EMPTY_MESSAGE = 'Chưa có dữ liệu trong khoảng này';

// Trang đơn hủy theo kỳ. Chỉ có một biểu đồ cột và bảng số liệu của nó.
export function CancellationsPeriodsReportPage() {
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
    return <ForbiddenNotice permission="reports:read" subject="báo cáo đơn hủy theo kỳ" />;
  }

  const buckets = periodQuery.data?.buckets ?? [];
  const points: ReportBarPoint[] = buckets.map((bucket) => ({
    key: bucket.key,
    label: formatBucketLabel(bucket.from, bucket.to, params.granularity),
    tick: formatBucketTick(bucket.from, params.granularity),
    // Cột hủy cộng cả ba lý do, vì trang này thống kê chung số đơn hủy.
    value: totalCancelledOrders(bucket.cancellations),
  }));
  const hasCancellations = points.some((point) => point.value > 0);
  const periodRangeLabel =
    REPORT_RANGE_OPTIONS.find((option) => option.value === params.range)?.label ?? '';
  // Ghi chú nêu khoảng ngày thật của response, để người đọc đối chiếu với biểu đồ.
  const periodNote = periodQuery.data
    ? `${periodRangeLabel}, ${formatReportDate(periodQuery.data.from)} - ${formatReportDate(periodQuery.data.to)}, giờ Việt Nam`
    : periodRangeLabel;

  return (
    <ReportPageFrame
      title="Báo cáo đơn hủy theo kỳ"
      description="Số đơn hủy, tính theo ngày tạo đơn và tổng hợp theo từng kỳ. Mọi ngày và tháng tính theo giờ Việt Nam."
      controls={
        <ReportPeriodControl
          range={params.range}
          granularity={params.granularity}
          onRangeChange={params.setRange}
          onGranularityChange={params.setGranularity}
        />
      }
    >
      <section className="card report-panel" aria-labelledby="cancellations-period-title">
        <div className="card-head report-panel-head">
          <h2 id="cancellations-period-title">Đơn hủy theo kỳ</h2>
          <span className="report-note muted">{periodNote}</span>
        </div>

        {periodQuery.isLoading && (
          <p className="report-status" role="status">
            Đang tải đơn hủy theo kỳ...
          </p>
        )}
        {periodQuery.isError && (
          <ErrorAlert message={periodQuery.error?.message ?? 'Không tải được đơn hủy theo kỳ.'} />
        )}
        {periodQuery.isSuccess && !hasCancellations && (
          <p className="empty-state">{PERIOD_EMPTY_MESSAGE}</p>
        )}

        {periodQuery.isSuccess && hasCancellations && (
          <ReportBarChart
            chartLabel={`Đơn hủy theo kỳ, ${periodRangeLabel}`}
            points={points}
            formatAxisValue={formatCount}
            tableCaption={`Bảng đơn hủy theo kỳ, ${periodRangeLabel}`}
            tableHead={
              <>
                <th scope="col">Kỳ</th>
                <th scope="col" className="num">
                  Số đơn hủy
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
