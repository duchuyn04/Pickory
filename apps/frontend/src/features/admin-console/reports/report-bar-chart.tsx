import type { ReactNode } from 'react';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, XAxis, YAxis } from 'recharts';
import './reports.css';

// Một cột của biểu đồ. `value` là số liệu của trang, ví dụ doanh thu của một tháng.
export interface ReportBarPoint {
  key: string;
  // Nhãn đầy đủ dùng trong bảng số liệu.
  label: string;
  // Nhãn ngắn trên trục hoành.
  tick: string;
  value: number;
}

interface ReportBarChartProps {
  chartLabel: string;
  points: ReportBarPoint[];
  // Rút gọn số trên trục tung để nhãn không chiếm hết chỗ của cột.
  formatAxisValue: (value: number) => string;
  tableCaption: string;
  tableHead: ReactNode;
  renderRow: (point: ReportBarPoint) => ReactNode;
  // Kích thước cố định cho test jsdom. Trang thật để trống để biểu đồ theo bề rộng thẻ.
  width?: number;
  height?: number;
}

// Biểu đồ cột có bảng số liệu đọc bằng trình đọc màn hình.
// Cột không nhận focus vì trang chỉ trình bày số liệu, không mở thêm chi tiết.
export function ReportBarChart({
  chartLabel,
  points,
  formatAxisValue,
  tableCaption,
  tableHead,
  renderRow,
  width,
  height,
}: ReportBarChartProps) {
  const chart = (
    <BarChart
      width={width}
      height={height}
      data={points}
      accessibilityLayer={false}
      margin={{ top: 8, right: 8, bottom: 0, left: 0 }}
    >
      <CartesianGrid vertical={false} stroke="var(--color-border-subtle)" />
      <XAxis
        dataKey="tick"
        tickLine={false}
        axisLine={{ stroke: 'var(--color-border-strong)' }}
        tick={{ fill: 'var(--color-ink-muted)' }}
        // Sáu cột mặc định hiện đủ nhãn. Khoảng chia theo ngày để Recharts bỏ bớt nhãn chồng nhau.
        interval={points.length <= 8 ? 0 : 'preserveStartEnd'}
        minTickGap={12}
      />
      <YAxis
        width={60}
        tickLine={false}
        axisLine={false}
        tickFormatter={formatAxisValue}
        tick={{ fill: 'var(--color-ink-muted)' }}
      />
      <Bar
        dataKey="value"
        fill="var(--color-chart-1)"
        radius={[4, 4, 0, 0]}
        // Tắt hiệu ứng để ảnh chụp và test thấy ngay số liệu cuối cùng.
        isAnimationActive={false}
      />
    </BarChart>
  );

  return (
    <div className="report-chart-block">
      <div className="report-chart-canvas" role="img" aria-label={chartLabel}>
        {width !== undefined && height !== undefined ? (
          chart
        ) : (
          <ResponsiveContainer width="100%" height={320}>
            {chart}
          </ResponsiveContainer>
        )}
      </div>
      <div className="table-wrap">
        <table className="tbl report-table">
          <caption>{tableCaption}</caption>
          <thead>
            <tr>{tableHead}</tr>
          </thead>
          <tbody>{points.map((point) => renderRow(point))}</tbody>
        </table>
      </div>
    </div>
  );
}
