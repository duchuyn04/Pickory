import {
  isReportGranularity,
  isReportRange,
  REPORT_GRANULARITY_OPTIONS,
  REPORT_RANGE_OPTIONS,
  type ReportGranularityCode,
  type ReportMonthOption,
  type ReportRangeCode,
} from './report-period';
import './reports.css';

interface ReportMonthControlProps {
  month: string;
  monthOptions: ReportMonthOption[];
  onChange: (month: string) => void;
}

// Ô chọn tháng của trang doanh thu theo thương hiệu. `select` gốc nên dùng được bằng bàn phím.
export function ReportMonthControl({ month, monthOptions, onChange }: ReportMonthControlProps) {
  return (
    <div className="report-controls">
      <div className="field">
        <label htmlFor="report-month">Tháng</label>
        <select id="report-month" value={month} onChange={(event) => onChange(event.target.value)}>
          {monthOptions.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>
    </div>
  );
}

interface ReportPeriodControlProps {
  range: ReportRangeCode;
  granularity: ReportGranularityCode;
  onRangeChange: (range: ReportRangeCode) => void;
  onGranularityChange: (granularity: ReportGranularityCode) => void;
}

// Hai ô chọn thời gian của trang doanh thu theo kỳ, cùng dùng `select` gốc.
export function ReportPeriodControl({
  range,
  granularity,
  onRangeChange,
  onGranularityChange,
}: ReportPeriodControlProps) {
  return (
    <div className="report-controls">
      <div className="field">
        <label htmlFor="report-range">Khoảng thời gian</label>
        <select
          id="report-range"
          value={range}
          onChange={(event) => {
            // Giá trị của `select` là chuỗi. Chỉ nhận mã đã khai báo để kiểu dữ liệu đúng.
            if (isReportRange(event.target.value)) onRangeChange(event.target.value);
          }}
        >
          {REPORT_RANGE_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>
      <div className="field">
        <label htmlFor="report-granularity">Chia theo</label>
        <select
          id="report-granularity"
          value={granularity}
          onChange={(event) => {
            if (isReportGranularity(event.target.value)) {
              onGranularityChange(event.target.value);
            }
          }}
        >
          {REPORT_GRANULARITY_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>
    </div>
  );
}
