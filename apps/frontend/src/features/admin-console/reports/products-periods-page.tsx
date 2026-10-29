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

// Trang sản phẩm bán ra theo kỳ. Chỉ có một biểu đồ cột và bảng số liệu của nó.
export function ProductsPeriodsReportPage() {
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
    return <ForbiddenNotice permission="reports:read" subject="báo cáo sản phẩm bán ra theo kỳ" />;
  }

  const buckets = periodQuery.data?.buckets ?? [];
  const points: ReportBarPoint[] = buckets.map((bucket) => ({
    key: bucket.key,
    label: formatBucketLabel(bucket.from, bucket.to, params.granularity),
    tick: formatBucketTick(bucket.from, params.granularity),
    value: bucket.soldQuantity,
  }));
  const hasQuantity = points.some((point) => point.value > 0);
  const periodRangeLabel =
    REPORT_RANGE_OPTIONS.find((option) => option.value === params.range)?.label ?? '';
  // Ghi chú nêu khoảng ngày thật của response, để người đọc đối chiếu với biểu đồ.
  const periodNote = periodQuery.data
    ? `${periodRangeLabel}, ${formatReportDate(periodQuery.data.from)} - ${formatReportDate(periodQuery.data.to)}, giờ Việt Nam`
    : periodRangeLabel;

  return (
    <ReportPageFrame
      title="Báo cáo sản phẩm bán ra theo kỳ"
      description="Số lượng sản phẩm bán ra của các đơn có doanh thu ghi nhận, tổng hợp theo từng kỳ. Mọi ngày và tháng tính theo giờ Việt Nam."
      controls={
        <ReportPeriodControl
          range={params.range}
          granularity={params.granularity}
          onRangeChange={params.setRange}
          onGranularityChange={params.setGranularity}
        />
      }
    >
      <section className="card report-panel" aria-labelledby="products-period-title">
        <div className="card-head report-panel-head">
          <h2 id="products-period-title">Sản phẩm bán ra theo kỳ</h2>
          <span className="report-note muted">{periodNote}</span>
        </div>

        {periodQuery.isLoading && (
          <p className="report-status" role="status">
            Đang tải sản phẩm bán ra theo kỳ...
          </p>
        )}
        {periodQuery.isError && (
          <ErrorAlert
            message={periodQuery.error?.message ?? 'Không tải được sản phẩm bán ra theo kỳ.'}
          />
        )}
        {periodQuery.isSuccess && !hasQuantity && (
          <p className="empty-state">{PERIOD_EMPTY_MESSAGE}</p>
        )}

        {periodQuery.isSuccess && hasQuantity && (
          <ReportBarChart
            chartLabel={`Sản phẩm bán ra theo kỳ, ${periodRangeLabel}`}
            points={points}
            formatAxisValue={formatCount}
            tableCaption={`Bảng sản phẩm bán ra theo kỳ, ${periodRangeLabel}`}
            tableHead={
              <>
                <th scope="col">Kỳ</th>
                <th scope="col" className="num">
                  Số lượng
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
