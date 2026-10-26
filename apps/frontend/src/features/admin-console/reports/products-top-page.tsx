import { useQuery } from '@tanstack/react-query';
import { ErrorAlert } from '../../../shared/error-alert';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { formatCount, formatReportMonth, formatReportMonthTitle, formatVnd } from './report-format';
import {
  brandLabel,
  buildProductSlices,
  OTHER_PRODUCT_KEY,
  productLabel,
  splitTopProducts,
} from './report-groups';
import { ReportPageFrame } from './report-page-frame';
import { ReportPieChart } from './report-pie-chart';
import { ReportMonthControl } from './report-period-controls';
import { fetchSalesReport } from './reports-api';
import { useMonthReportParams } from './use-report-params';
import './reports.css';

const MONTH_EMPTY_MESSAGE = 'Chưa có dữ liệu trong tháng này';

// Trang sản phẩm bán ra của một tháng, theo từng sản phẩm. Chỉ có một biểu đồ và bảng số liệu của nó.
export function ProductsTopReportPage() {
  const canReadReports = useCan('reports:read');
  const params = useMonthReportParams('product');

  const monthQuery = useQuery({
    queryKey: ['admin-sales-report', 'month', params.month],
    queryFn: () => fetchSalesReport({ range: 'month', month: params.month, granularity: 'day' }),
    enabled: canReadReports,
  });

  // Hook ở trên phải chạy hết trước lệnh return sớm này. Backend vẫn chặn mọi request thật.
  if (!canReadReports) {
    return <ForbiddenNotice permission="reports:read" subject="báo cáo sản phẩm bán ra" />;
  }

  const split = splitTopProducts(monthQuery.data?.topProducts ?? []);
  const slices = buildProductSlices(split);
  const total = slices.reduce((sum, slice) => sum + slice.value, 0);
  // Sản phẩm đang chọn đọc từ URL, nên mở lại liên kết vẫn thấy bảng chi tiết của sản phẩm đó.
  const selectedProduct =
    params.selection && params.selection !== OTHER_PRODUCT_KEY
      ? (split.top.find((item) => item.productId === params.selection) ?? null)
      : null;
  const listsOtherGroup = params.selection === OTHER_PRODUCT_KEY && split.rest.length > 0;
  const detailRows = selectedProduct ? [selectedProduct] : split.rest;

  return (
    <ReportPageFrame
      title="Báo cáo sản phẩm bán ra theo sản phẩm"
      description="Số lượng sản phẩm bán ra của các đơn có doanh thu ghi nhận trong tháng. Năm sản phẩm bán nhiều nhất hiện riêng, phần còn lại gộp vào nhóm Khác."
      controls={
        <ReportMonthControl
          month={params.month}
          monthOptions={params.monthOptions}
          onChange={params.setMonth}
        />
      }
    >
      <section className="card report-panel" aria-labelledby="products-top-title">
        <div className="card-head report-panel-head">
          <h2 id="products-top-title">Sản phẩm bán ra</h2>
          <span className="report-note muted">
            {formatReportMonthTitle(params.month)}, giờ Việt Nam
          </span>
        </div>

        {monthQuery.isLoading && (
          <p className="report-status" role="status">
            Đang tải sản phẩm bán ra...
          </p>
        )}
        {monthQuery.isError && (
          <ErrorAlert message={monthQuery.error?.message ?? 'Không tải được sản phẩm bán ra.'} />
        )}
        {monthQuery.isSuccess && total === 0 && (
          <p className="empty-state">{MONTH_EMPTY_MESSAGE}</p>
        )}

        {monthQuery.isSuccess && total > 0 && (
          <>
            <ReportPieChart
              chartLabel={`Sản phẩm bán ra, ${formatReportMonth(params.month)}`}
              slices={slices}
              neutralKey={OTHER_PRODUCT_KEY}
              selectedKey={params.selection || null}
              onSliceSelect={params.selectGroup}
              tableCaption={`Bảng sản phẩm bán ra, ${formatReportMonth(params.month)}`}
              tableHead={
                <>
                  <th scope="col">Sản phẩm</th>
                  <th scope="col" className="num">
                    Số lượng
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
            {(selectedProduct || listsOtherGroup) && (
              <div className="report-drilldown">
                <h3>
                  {selectedProduct
                    ? `Chi tiết ${productLabel(selectedProduct)}`
                    : 'Các sản phẩm trong nhóm Khác'}
                </h3>
                <div className="table-wrap">
                  <table className="tbl report-table">
                    <caption>
                      {selectedProduct
                        ? `Bảng chi tiết ${productLabel(selectedProduct)}, ${formatReportMonth(params.month)}`
                        : `Bảng sản phẩm trong nhóm Khác, ${formatReportMonth(params.month)}`}
                    </caption>
                    <thead>
                      <tr>
                        <th scope="col">Sản phẩm</th>
                        <th scope="col">Thương hiệu</th>
                        <th scope="col" className="num">
                          Doanh thu
                        </th>
                        <th scope="col" className="num">
                          Số lượng
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {detailRows.map((item) => (
                        <tr key={item.productId}>
                          <th scope="row">{productLabel(item)}</th>
                          <td>{brandLabel(item)}</td>
                          <td className="num">{formatVnd(item.revenue)}</td>
                          <td className="num">{formatCount(item.quantity)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )}
          </>
        )}
      </section>
    </ReportPageFrame>
  );
}
