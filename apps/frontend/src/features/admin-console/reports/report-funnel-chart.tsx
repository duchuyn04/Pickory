import { Bar, BarChart, CartesianGrid, ResponsiveContainer, XAxis, YAxis } from 'recharts';
import { formatCount } from './report-format';
import './reports.css';

// Một bước của phễu. `count` là số lượt của bước, `rateLabel` là tỉ lệ chuyển đã thành chữ.
export interface ReportFunnelPoint {
  key: string;
  // Tên bước, ví dụ "Xem sản phẩm".
  label: string;
  count: number;
  // Tỉ lệ so với bước trước, hoặc câu thay thế khi không có tỉ lệ.
  rateLabel: string;
}

interface ReportFunnelChartProps {
  // Tên của biểu đồ cho trình đọc màn hình. Tiêu đề nhìn thấy nằm ở thẻ chứa biểu đồ.
  chartLabel: string;
  points: ReportFunnelPoint[];
  tableCaption: string;
  // Kích thước cố định cho test jsdom. Trang thật để trống để biểu đồ theo bề rộng thẻ.
  width?: number;
  height?: number;
}

// Biểu đồ phễu có bảng số liệu đọc bằng trình đọc màn hình.
// Thanh ngang dài theo số lượt nên ba bước xếp dọc như một phễu.
// Thanh không nhận focus vì trang chỉ trình bày số liệu, không mở thêm chi tiết.
export function ReportFunnelChart({
  chartLabel,
  points,
  tableCaption,
  width,
  height,
}: ReportFunnelChartProps) {
  const chart = (
    <BarChart
      width={width}
      height={height}
      data={points}
      layout="vertical"
      accessibilityLayer={false}
      margin={{ top: 8, right: 16, bottom: 0, left: 0 }}
    >
      <CartesianGrid horizontal={false} stroke="var(--color-border-subtle)" />
      <XAxis
        type="number"
        tickLine={false}
        axisLine={false}
        tickFormatter={formatCount}
        tick={{ fill: 'var(--color-ink-muted)' }}
        // Bộ đếm là số nguyên, nên không chia trục thành phần lẻ.
        allowDecimals={false}
      />
      <YAxis
        type="category"
        dataKey="label"
        width={104}
        tickLine={false}
        axisLine={{ stroke: 'var(--color-border-strong)' }}
        tick={{ fill: 'var(--color-ink-muted)' }}
      />
      <Bar
        dataKey="count"
        fill="var(--color-chart-1)"
        radius={[0, 4, 4, 0]}
        barSize={28}
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
            <tr>
              <th scope="col">Bước</th>
              <th scope="col" className="num">
                Lượt
              </th>
              <th scope="col" className="num">
                Tỉ lệ chuyển
              </th>
            </tr>
          </thead>
          <tbody>
            {points.map((point) => (
              <tr key={point.key}>
                <th scope="row">{point.label}</th>
                <td className="num">{formatCount(point.count)}</td>
                <td className="num">{point.rateLabel}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
