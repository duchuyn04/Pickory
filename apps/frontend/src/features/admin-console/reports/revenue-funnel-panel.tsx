import { useQuery } from '@tanstack/react-query';
import { ErrorAlert } from '../../../shared/error-alert';
import { funnelRows } from './funnel-steps';
import { formatReportDate } from './report-format';
import { ReportFunnelChart } from './report-funnel-chart';
import { fetchFunnelReport } from './reports-api';
import './reports.css';

interface RevenueFunnelPanelProps {
  // Khoảng ngày lịch Việt Nam của báo cáo doanh thu. Khối phễu dùng đúng khoảng đó.
  from: string;
  to: string;
}

// Khối phễu chuyển đổi của trang doanh thu theo kỳ. Khối tự tải dữ liệu, nên lỗi của route phễu
// chỉ thay nội dung khối này, không thay biểu đồ doanh thu bên trên.
export function RevenueFunnelPanel({ from, to }: RevenueFunnelPanelProps) {
  // Khóa truy vấn gồm khoảng ngày, nên đổi khoảng của trang là phễu tải lại theo khoảng mới.
  const funnelQuery = useQuery({
    queryKey: ['admin-funnel-report', from, to],
    queryFn: () => fetchFunnelReport({ from, to }),
  });

  const rangeLabel = `${formatReportDate(from)} đến ${formatReportDate(to)}`;
  const rows = funnelQuery.data ? funnelRows(funnelQuery.data.steps) : [];

  return (
    <section className="card report-panel" aria-labelledby="revenue-funnel-title">
      <div className="card-head report-panel-head">
        <h2 id="revenue-funnel-title">Phễu chuyển đổi</h2>
        <span className="report-note muted">{rangeLabel}</span>
      </div>

      {funnelQuery.isLoading && (
        <p className="report-status" role="status">
          Đang tải phễu chuyển đổi...
        </p>
      )}
      {funnelQuery.isError && (
        <ErrorAlert message={funnelQuery.error?.message ?? 'Không tải được phễu chuyển đổi.'} />
      )}
      {funnelQuery.isSuccess && (
        <ReportFunnelChart
          chartLabel={`Phễu chuyển đổi từ ${rangeLabel}`}
          points={rows}
          tableCaption={`Bảng phễu chuyển đổi từ ${rangeLabel}`}
        />
      )}
    </section>
  );
}
