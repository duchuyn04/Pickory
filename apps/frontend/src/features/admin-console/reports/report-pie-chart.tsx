import type { ReactNode } from 'react';
import {
  Pie,
  PieChart,
  ResponsiveContainer,
  Sector,
  type PieLabelRenderProps,
  type PieSectorShapeProps,
} from 'recharts';
import { reportSeriesColor } from './report-colors';
import { formatPercent, formatPercentValue } from './report-format';
import './reports.css';

// Một lát của biểu đồ tròn. `value` là số liệu của trang, ví dụ doanh thu của một thương hiệu.
export interface ReportPieSlice {
  key: string;
  label: string;
  value: number;
}

interface ReportPieChartProps {
  // Tên của biểu đồ cho trình đọc màn hình. Tiêu đề nhìn thấy nằm ở thẻ chứa biểu đồ.
  chartLabel: string;
  slices: ReportPieSlice[];
  // Khóa của nhóm gộp "Khác". Nhóm này dùng màu trung tính thay vì màu phân loại.
  neutralKey?: string;
  tableCaption: string;
  // Mỗi phần tử là một ô tiêu đề cột. Biểu đồ bọc chúng trong một dòng `tr`.
  tableHead: ReactNode;
  // Trả về một dòng `tr` của bảng số liệu. Trang quyết định các cột cần hiện.
  renderRow: (slice: ReportPieSlice, percent: string) => ReactNode;
  // Kích thước cố định cho test jsdom. Trang thật để trống để biểu đồ theo bề rộng thẻ.
  width?: number;
  height?: number;
}

interface PieDatum extends ReportPieSlice {
  fill: string;
  // Không đặt tên `percent` vì Recharts dùng chính tên đó cho tỉ lệ số của lát.
  percentLabel: string;
}

// Đổi góc của Recharts sang radian để đặt nhãn phần trăm trong lát.
const DEGREE_TO_RADIAN = Math.PI / 180;

function renderPercentLabel(props: PieLabelRenderProps) {
  const { cx = 0, cy = 0, midAngle = 0, innerRadius = 0, outerRadius = 0, percent = 0 } = props;
  // Đặt nhãn ở giữa dải vành khuyên, không sát mép trong hay mép ngoài.
  const radius = innerRadius + (outerRadius - innerRadius) * 0.55;
  const x = cx + radius * Math.cos(-midAngle * DEGREE_TO_RADIAN);
  const y = cy + radius * Math.sin(-midAngle * DEGREE_TO_RADIAN);
  // Nhãn của lát đã làm tròn sẵn. `percent` của Recharts là tỉ lệ số thô.
  const datum = props.payload as PieDatum | undefined;
  return (
    <text
      x={x}
      y={y}
      textAnchor="middle"
      dominantBaseline="central"
      className="report-chart-label"
      aria-hidden="true"
    >
      {datum?.percentLabel ?? formatPercentValue(percent)}
    </text>
  );
}

// Biểu đồ tròn có nhãn phần trăm, chú giải và bảng số liệu đọc bằng trình đọc màn hình.
// Lát không nhận focus vì trang chỉ trình bày số liệu, không mở thêm chi tiết.
export function ReportPieChart({
  chartLabel,
  slices,
  neutralKey,
  tableCaption,
  tableHead,
  renderRow,
  width,
  height,
}: ReportPieChartProps) {
  const total = slices.reduce((sum, slice) => sum + slice.value, 0);
  // Tổng bằng 0 thì trang đã hiện thông báo trống. Không vẽ biểu đồ rỗng ở đây.
  if (total <= 0) return null;

  const data: PieDatum[] = slices.map((slice, index) => ({
    ...slice,
    fill: reportSeriesColor(index, slice.key === neutralKey),
    percentLabel: formatPercent(slice.value, total),
  }));

  const renderSector = (sector: PieSectorShapeProps) => {
    // Recharts truyền dữ liệu gốc của lát qua `payload`.
    const datum = sector.payload as PieDatum | undefined;
    if (!datum) return <g />;
    return (
      <Sector
        cx={sector.cx}
        cy={sector.cy}
        innerRadius={sector.innerRadius}
        outerRadius={sector.outerRadius}
        startAngle={sector.startAngle}
        endAngle={sector.endAngle}
        fill={datum.fill}
        stroke="var(--color-surface)"
        strokeWidth={2}
      />
    );
  };

  const chart = (
    <PieChart width={width} height={height} accessibilityLayer={false}>
      <Pie
        data={data}
        dataKey="value"
        nameKey="label"
        innerRadius="46%"
        outerRadius="78%"
        startAngle={90}
        endAngle={-270}
        // Tắt hiệu ứng để ảnh chụp và test thấy ngay số liệu cuối cùng.
        isAnimationActive={false}
        // Biểu đồ không có phần tử nhận focus. Bảng bên dưới là nơi đọc số liệu.
        rootTabIndex={-1}
        label={renderPercentLabel}
        labelLine={false}
        shape={renderSector}
      />
    </PieChart>
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
      <ul className="report-chart-legend" aria-label={`Chú giải cho ${chartLabel}`}>
        {data.map((datum) => (
          <li key={datum.key} className="report-legend-item">
            {/* Ô màu vẽ bằng `fill` của SVG, không dùng style trực tiếp trong component. */}
            <svg
              className="report-legend-swatch"
              viewBox="0 0 12 12"
              aria-hidden="true"
              focusable="false"
            >
              <rect width="12" height="12" rx="3" fill={datum.fill} />
            </svg>
            <span className="report-legend-name">{datum.label}</span>
            <span className="report-legend-percent">{datum.percentLabel}</span>
          </li>
        ))}
      </ul>
      <div className="table-wrap">
        <table className="tbl report-table">
          <caption>{tableCaption}</caption>
          <thead>
            <tr>{tableHead}</tr>
          </thead>
          <tbody>
            {slices.map((slice) => renderRow(slice, formatPercent(slice.value, total)))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
