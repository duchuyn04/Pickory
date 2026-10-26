import { useQuery } from '@tanstack/react-query';
import { ErrorAlert } from '../../../shared/error-alert';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { buildBrandSlices, OTHER_BRAND_KEY, totalBrandRevenue } from './brand-slices';
import { formatReportMonth, formatReportMonthTitle, formatVnd } from './report-format';
import { ReportPageFrame } from './report-page-frame';
import { ReportMonthControl } from './report-period-controls';
import { ReportPieChart } from './report-pie-chart';
import { fetchSalesReport } from './reports-api';
import { useBrandReportParams } from './use-report-params';
import './reports.css';

const MONTH_EMPTY_MESSAGE = 'Chưa có dữ liệu trong tháng này';

// Trang doanh thu theo thương hiệu của một tháng. Chỉ có một biểu đồ và bảng số liệu của nó.
export function RevenueBrandsReportPage() {
  const canReadReports = useCan('reports:read');
  const params = useBrandReportParams();

  const monthQuery = useQuery({
    queryKey: ['admin-sales-report', 'month', params.month],
    queryFn: () => fetchSalesReport({ range: 'month', month: params.month, granularity: 'day' }),
    enabled: canReadReports,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này. Backend vẫn chặn mọi request thật.
  if (!canReadReports) {
    return (
      <ForbiddenNotice permission="reports:read" subject="báo cáo doanh thu theo thương hiệu" />
    );
  }

  const slices = buildBrandSlices(monthQuery.data?.brands ?? []);
  const total = totalBrandRevenue(slices);

  return (
    <ReportPageFrame
      title="Báo cáo doanh thu theo thương hiệu"
      description="Doanh thu ghi nhận theo ngày thanh toán, chia theo thương hiệu trong tháng. Mọi ngày tính theo giờ Việt Nam."
      controls={
        <ReportMonthControl
          month={params.month}
          monthOptions={params.monthOptions}
          onChange={params.setMonth}
        />
      }
    >
      <section className="card report-panel" aria-labelledby="revenue-brand-title">
        <div className="card-head report-panel-head">
          <h2 id="revenue-brand-title">Doanh thu theo thương hiệu</h2>
          <span className="report-note muted">
            {formatReportMonthTitle(params.month)}, giờ Việt Nam
          </span>
        </div>

        {monthQuery.isLoading && (
          <p className="report-status" role="status">
            Đang tải doanh thu theo thương hiệu...
          </p>
        )}
        {monthQuery.isError && (
          <ErrorAlert
            message={monthQuery.error?.message ?? 'Không tải được doanh thu theo thương hiệu.'}
          />
        )}
        {monthQuery.isSuccess && total === 0 && (
          <p className="empty-state">{MONTH_EMPTY_MESSAGE}</p>
        )}

        {monthQuery.isSuccess && total > 0 && (
          <ReportPieChart
            chartLabel={`Doanh thu theo thương hiệu ${formatReportMonth(params.month)}`}
            slices={slices.map((slice) => ({
              key: slice.key,
              label: slice.label,
              value: slice.revenue,
            }))}
            neutralKey={OTHER_BRAND_KEY}
            tableCaption={`Bảng doanh thu theo thương hiệu, ${formatReportMonth(params.month)}`}
            tableHead={
              <>
                <th scope="col">Thương hiệu</th>
                <th scope="col" className="num">
                  Doanh thu
                </th>
                <th scope="col" className="num">
                  Tỷ lệ
                </th>
              </>
            }
            renderRow={(slice, percent) => (
              <tr key={slice.key}>
                <th scope="row">{slice.label}</th>
                <td className="num">{formatVnd(slice.value)}</td>
                <td className="num">{percent}</td>
              </tr>
            )}
          />
        )}
      </section>
    </ReportPageFrame>
  );
}
