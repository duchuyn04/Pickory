import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import { ErrorAlert } from '../../../shared/error-alert';
import { ForbiddenNotice } from '../../../shared/forbidden-notice';
import { useCan } from '../../identity/hooks/use-actor';
import { buildBrandSlices, OTHER_BRAND_KEY } from './brand-slices';
import { formatCount, formatReportMonth, formatReportMonthTitle, formatVnd } from './report-format';
import {
  buildCancellationSlices,
  buildPaymentMethodSlices,
  buildProductSlices,
  OTHER_PRODUCT_KEY,
  splitTopProducts,
} from './report-groups';
import { currentVietnamMonth } from './report-period';
import { ReportPieChart, type ReportPieSlice } from './report-pie-chart';
import { fetchSalesReport, type SalesReportData } from './reports-api';
import './reports.css';

const MONTH_EMPTY_MESSAGE = 'Chưa có dữ liệu trong tháng này';

// Mô hình của một thẻ: các lát, tổng của tháng và cách định dạng số của báo cáo đó.
interface CardModel {
  slices: ReportPieSlice[];
  total: number;
  valueLabel: string;
  formatValue: (value: number) => string;
  // Khóa nhóm gộp dùng màu trung tính, chỉ báo cáo sản phẩm mới có.
  neutralKey?: string;
}

interface CardDefinition {
  id: string;
  title: string;
  // Nhãn của cột phân loại trong bảng của thẻ.
  groupLabel: string;
  totalLabel: string;
  detailPath: string;
  // Tên tham số mang lát đang chọn sang trang chi tiết.
  selectionParam: string;
  read: (data: SalesReportData) => CardModel;
}

// Tổng của thẻ bằng tổng các lát, nên tổng và biểu đồ không lệch nhau khi một nhóm bị gộp.
function sumSlices(slices: readonly ReportPieSlice[]): number {
  return slices.reduce((sum, slice) => sum + slice.value, 0);
}

// Bốn báo cáo của quyết định 23. Mỗi thẻ đọc một loại số liệu và mở đúng trang chi tiết của nó.
const REPORT_CARDS: readonly CardDefinition[] = [
  {
    id: 'revenue',
    title: 'Doanh thu',
    groupLabel: 'Thương hiệu',
    totalLabel: 'Tổng doanh thu tháng này',
    detailPath: '/admin/reports/revenue/brands',
    selectionParam: 'brand',
    read: (data) => {
      const slices = buildBrandSlices(data.brands).map((slice) => ({
        key: slice.key,
        label: slice.label,
        value: slice.revenue,
      }));
      return {
        slices,
        total: sumSlices(slices),
        valueLabel: 'Doanh thu',
        formatValue: formatVnd,
        neutralKey: OTHER_BRAND_KEY,
      };
    },
  },
  {
    id: 'orders',
    title: 'Đơn đã thanh toán',
    groupLabel: 'Phương thức',
    totalLabel: 'Tổng đơn đã thanh toán tháng này',
    detailPath: '/admin/reports/orders/payments',
    selectionParam: 'method',
    read: (data) => {
      const slices = buildPaymentMethodSlices(data.totals.paidByMethod);
      return { slices, total: sumSlices(slices), valueLabel: 'Số đơn', formatValue: formatCount };
    },
  },
  {
    id: 'cancellations',
    title: 'Đơn hủy',
    groupLabel: 'Lý do',
    totalLabel: 'Tổng đơn hủy tháng này',
    detailPath: '/admin/reports/cancellations/reasons',
    selectionParam: 'reason',
    read: (data) => {
      const slices = buildCancellationSlices(data.totals.cancellations);
      return {
        slices,
        total: sumSlices(slices),
        valueLabel: 'Số đơn hủy',
        formatValue: formatCount,
      };
    },
  },
  {
    id: 'products',
    title: 'Sản phẩm bán ra',
    groupLabel: 'Sản phẩm',
    totalLabel: 'Tổng sản phẩm bán ra tháng này',
    detailPath: '/admin/reports/products/top',
    selectionParam: 'product',
    read: (data) => {
      const slices = buildProductSlices(splitTopProducts(data.topProducts));
      return {
        slices,
        total: sumSlices(slices),
        valueLabel: 'Số lượng',
        formatValue: formatCount,
        neutralKey: OTHER_PRODUCT_KEY,
      };
    },
  },
];

interface ReportCardProps {
  card: CardDefinition;
  // Tháng của dashboard. Cả bốn thẻ dùng chung một tháng để số liệu so sánh được.
  month: string;
}

// Một thẻ báo cáo: biểu đồ tròn của tháng, tổng của tháng và liên kết tới trang chi tiết.
function ReportCard({ card, month }: ReportCardProps) {
  const navigate = useNavigate();
  const detailHref = `${card.detailPath}?month=${month}`;

  // Mỗi thẻ gọi báo cáo riêng, để một thẻ lỗi không làm trống ba thẻ còn lại.
  const query = useQuery({
    queryKey: ['admin-sales-report', 'card', card.id, month],
    queryFn: () => fetchSalesReport({ range: 'month', month, granularity: 'day' }),
  });

  const model = query.data ? card.read(query.data) : null;
  const hasData = model !== null && model.total > 0;
  const lowerTitle = card.title.toLowerCase();

  return (
    <article className="card report-card" aria-labelledby={`report-card-${card.id}`}>
      <div className="card-head report-panel-head">
        <h2 id={`report-card-${card.id}`}>
          {/* Link trải kín thẻ bằng lớp phủ trong `reports.css`, nên nhấn vào thẻ là mở báo cáo. */}
          <Link className="report-card-link" to={detailHref}>
            {card.title}
          </Link>
        </h2>
        <span className="report-note muted">{formatReportMonthTitle(month)}</span>
      </div>

      {query.isLoading && (
        <p className="report-status" role="status">
          Đang tải {lowerTitle}...
        </p>
      )}
      {query.isError && (
        <ErrorAlert message={query.error?.message ?? `Không tải được ${lowerTitle}.`} />
      )}
      {query.isSuccess && model && !hasData && <p className="empty-state">{MONTH_EMPTY_MESSAGE}</p>}

      {hasData && model && (
        <>
          <p className="report-card-total">
            <span>{card.totalLabel}</span>
            <strong>{model.formatValue(model.total)}</strong>
          </p>
          <ReportPieChart
            chartLabel={`${card.title}, ${formatReportMonth(month)}`}
            slices={model.slices}
            neutralKey={model.neutralKey}
            // Chọn lát thì mở trang chi tiết với lát đó đã chọn sẵn.
            onSliceSelect={(key) =>
              navigate(`${detailHref}&${card.selectionParam}=${encodeURIComponent(key)}`)
            }
            tableCaption={`Bảng ${lowerTitle}, ${formatReportMonth(month)}`}
            tableHead={
              <>
                <th scope="col">{card.groupLabel}</th>
                <th scope="col" className="num">
                  {model.valueLabel}
                </th>
                <th scope="col" className="num">
                  Tỷ lệ
                </th>
              </>
            }
            renderRow={(slice, percent) => (
              <tr key={slice.key}>
                <th scope="row">{slice.label}</th>
                <td className="num">{model.formatValue(slice.value)}</td>
                <td className="num">{percent}</td>
              </tr>
            )}
          />
        </>
      )}
    </article>
  );
}

// Trang tổng quan báo cáo tại `/admin/reports`. Bốn thẻ, mỗi thẻ một báo cáo của tháng này.
export function ReportsDashboardPage() {
  const canReadReports = useCan('reports:read');
  const month = currentVietnamMonth();

  // Hook ở trên phải chạy hết trước lệnh return sớm này. Backend vẫn chặn mọi request thật.
  if (!canReadReports) {
    return <ForbiddenNotice permission="reports:read" subject="báo cáo cửa hàng" />;
  }

  return (
    <div className="page report-page">
      <div className="page-head">
        <div className="page-head-text">
          <h1>Báo cáo</h1>
          <p>
            Bốn báo cáo của {formatReportMonth(month)}, tính theo giờ Việt Nam. Nhấn một thẻ để mở
            báo cáo đầy đủ.
          </p>
        </div>
      </div>
      <div className="report-cards">
        {REPORT_CARDS.map((card) => (
          <ReportCard key={card.id} card={card} month={month} />
        ))}
      </div>
    </div>
  );
}
