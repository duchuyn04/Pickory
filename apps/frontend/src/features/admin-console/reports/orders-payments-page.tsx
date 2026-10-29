import { useQuery } from '@tanstack/react-query';
import { ErrorAlert } from '../../../shared/error-alert';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { ReportDailyDrilldown } from './report-daily-drilldown';
import { formatCount, formatReportMonth, formatReportMonthTitle } from './report-format';
import {
  buildPaymentMethodSlices,
  countPaymentMethodOrders,
  PAYMENT_METHOD_GROUPS,
} from './report-groups';
import { ReportPageFrame } from './report-page-frame';
import { ReportPieChart } from './report-pie-chart';
import { ReportMonthControl } from './report-period-controls';
import { fetchSalesReport } from './reports-api';
import { useMonthReportParams } from './use-report-params';
import './reports.css';

const MONTH_EMPTY_MESSAGE = 'Chưa có dữ liệu trong tháng này';

// Trang đơn đã thanh toán theo phương thức của một tháng. Chỉ có một biểu đồ và bảng số liệu của nó.
export function OrdersPaymentsReportPage() {
  const canReadReports = useCan('reports:read');
  const params = useMonthReportParams('method');

  const monthQuery = useQuery({
    queryKey: ['admin-sales-report', 'month', params.month],
    queryFn: () => fetchSalesReport({ range: 'month', month: params.month, granularity: 'day' }),
    enabled: canReadReports,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này. Backend vẫn chặn mọi request thật.
  if (!canReadReports) {
    return <ForbiddenNotice permission="reports:read" subject="báo cáo đơn đã thanh toán" />;
  }

  const slices = buildPaymentMethodSlices(
    monthQuery.data?.totals.paidByMethod ?? { cod: 0, sepay: 0 },
  );
  const total = slices.reduce((sum, slice) => sum + slice.value, 0);
  // Nhóm đang chọn đọc từ URL, cùng nguồn với nút chú giải đang nhấn.
  const group =
    PAYMENT_METHOD_GROUPS.find((candidate) => candidate.key === params.selection) ?? null;
  // Sau khi đổi tháng, nhóm đã chọn có thể không còn lát. Chỉ mở bảng chi tiết khi lát đó vẫn tồn tại.
  const activeGroup = group && slices.some((slice) => slice.key === group.key) ? group : null;

  return (
    <ReportPageFrame
      title="Báo cáo đơn đã thanh toán theo phương thức"
      description="Số đơn đã thanh toán theo phương thức, tính theo ngày thanh toán. Mọi ngày tính theo giờ Việt Nam."
      controls={
        <ReportMonthControl
          month={params.month}
          monthOptions={params.monthOptions}
          onChange={params.setMonth}
        />
      }
    >
      <section className="card report-panel" aria-labelledby="orders-payment-title">
        <div className="card-head report-panel-head">
          <h2 id="orders-payment-title">Đơn đã thanh toán theo phương thức</h2>
          <span className="report-note muted">
            {formatReportMonthTitle(params.month)}, giờ Việt Nam
          </span>
        </div>

        {monthQuery.isLoading && (
          <p className="report-status" role="status">
            Đang tải đơn đã thanh toán...
          </p>
        )}
        {monthQuery.isError && (
          <ErrorAlert message={monthQuery.error?.message ?? 'Không tải được đơn đã thanh toán.'} />
        )}
        {monthQuery.isSuccess && total === 0 && (
          <p className="empty-state">{MONTH_EMPTY_MESSAGE}</p>
        )}

        {monthQuery.isSuccess && total > 0 && (
          <>
            <ReportPieChart
              chartLabel={`Đơn đã thanh toán theo phương thức, ${formatReportMonth(params.month)}`}
              slices={slices}
              selectedKey={params.selection || null}
              onSliceSelect={params.selectGroup}
              tableCaption={`Bảng đơn đã thanh toán theo phương thức, ${formatReportMonth(params.month)}`}
              tableHead={
                <>
                  <th scope="col">Phương thức</th>
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
            {activeGroup && (
              <ReportDailyDrilldown
                title={`Chi tiết ${activeGroup.label} theo ngày, ${formatReportMonth(params.month)}`}
                caption={`Bảng số đơn ${activeGroup.label} theo ngày, ${formatReportMonth(params.month)}`}
                buckets={monthQuery.data.buckets}
                countOf={(bucket) => countPaymentMethodOrders(activeGroup.key, bucket.paidByMethod)}
              />
            )}
          </>
        )}
      </section>
    </ReportPageFrame>
  );
}
