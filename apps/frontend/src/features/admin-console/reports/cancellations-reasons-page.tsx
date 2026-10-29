import { useQuery } from '@tanstack/react-query';
import { ErrorAlert } from '../../../shared/error-alert';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { ReportDailyDrilldown } from './report-daily-drilldown';
import { formatCount, formatReportMonth, formatReportMonthTitle } from './report-format';
import {
  buildCancellationSlices,
  CANCELLATION_REASON_GROUPS,
  countCancelledOrders,
} from './report-groups';
import { ReportPageFrame } from './report-page-frame';
import { ReportPieChart } from './report-pie-chart';
import { ReportMonthControl } from './report-period-controls';
import { fetchSalesReport } from './reports-api';
import { useMonthReportParams } from './use-report-params';
import './reports.css';

const MONTH_EMPTY_MESSAGE = 'Chưa có dữ liệu trong tháng này';

// Trang đơn hủy theo lý do của một tháng. Chỉ có một biểu đồ và bảng số liệu của nó.
export function CancellationsReasonsReportPage() {
  const canReadReports = useCan('reports:read');
  const params = useMonthReportParams('reason');

  // Độ chia theo ngày để bảng chi tiết có số của từng ngày trong tháng.
  const monthQuery = useQuery({
    queryKey: ['admin-sales-report', 'month', params.month],
    queryFn: () => fetchSalesReport({ range: 'month', month: params.month, granularity: 'day' }),
    enabled: canReadReports,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này. Backend vẫn chặn mọi request thật.
  if (!canReadReports) {
    return <ForbiddenNotice permission="reports:read" subject="báo cáo đơn hủy" />;
  }

  const slices = buildCancellationSlices(
    monthQuery.data?.totals.cancellations ?? {
      byCustomer: 0,
      byShop: 0,
      paymentExpired: 0,
    },
  );
  const total = slices.reduce((sum, slice) => sum + slice.value, 0);
  // Lý do đang chọn đọc từ URL, cùng nguồn với nút chú giải đang nhấn.
  const reason =
    CANCELLATION_REASON_GROUPS.find((candidate) => candidate.key === params.selection) ?? null;
  // Sau khi đổi tháng, lý do đã chọn có thể không còn lát. Chỉ mở bảng chi tiết khi lát đó vẫn tồn tại.
  const activeReason = reason && slices.some((slice) => slice.key === reason.key) ? reason : null;

  return (
    <ReportPageFrame
      title="Báo cáo đơn hủy theo lý do"
      description="Số đơn hủy theo lý do, tính theo ngày tạo đơn. Mọi ngày tính theo giờ Việt Nam."
      controls={
        <ReportMonthControl
          month={params.month}
          monthOptions={params.monthOptions}
          onChange={params.setMonth}
        />
      }
    >
      <section className="card report-panel" aria-labelledby="cancellations-reason-title">
        <div className="card-head report-panel-head">
          <h2 id="cancellations-reason-title">Đơn hủy theo lý do</h2>
          <span className="report-note muted">
            {formatReportMonthTitle(params.month)}, giờ Việt Nam
          </span>
        </div>

        {monthQuery.isLoading && (
          <p className="report-status" role="status">
            Đang tải đơn hủy theo lý do...
          </p>
        )}
        {monthQuery.isError && (
          <ErrorAlert message={monthQuery.error?.message ?? 'Không tải được đơn hủy.'} />
        )}
        {monthQuery.isSuccess && total === 0 && (
          <p className="empty-state">{MONTH_EMPTY_MESSAGE}</p>
        )}

        {monthQuery.isSuccess && total > 0 && (
          <>
            <ReportPieChart
              chartLabel={`Đơn hủy theo lý do, ${formatReportMonth(params.month)}`}
              slices={slices}
              selectedKey={params.selection || null}
              onSliceSelect={params.selectGroup}
              tableCaption={`Bảng đơn hủy theo lý do, ${formatReportMonth(params.month)}`}
              tableHead={
                <>
                  <th scope="col">Lý do</th>
                  <th scope="col" className="num">
                    Số đơn
                  </th>
                  <th scope="col" className="num">
                    Tỷ lệ
                  </th>
                </>
              }
              renderRow={(slice, percent) => (
                <tr key={slice.key}>
                  <th scope="row">{slice.label}</th>
                  <td className="num">{formatCount(slice.value)}</td>
                  <td className="num">{percent}</td>
                </tr>
              )}
            />
            {activeReason && (
              <ReportDailyDrilldown
                title={`Chi tiết ${activeReason.label} theo ngày, ${formatReportMonth(params.month)}`}
                caption={`Bảng số đơn ${activeReason.label} theo ngày, ${formatReportMonth(params.month)}`}
                buckets={monthQuery.data.buckets}
                countOf={(bucket) => countCancelledOrders(activeReason.key, bucket.cancellations)}
              />
            )}
          </>
        )}
      </section>
    </ReportPageFrame>
  );
}
