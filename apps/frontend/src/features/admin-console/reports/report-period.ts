import { formatReportDate, formatReportMonthTitle } from './report-format';

// Giá trị của `range` khớp tham số API. `6m` là sáu tháng lịch gần nhất, gồm tháng này.
export const REPORT_RANGE_CODES = ['6m', '30d', '7d', 'this_month'] as const;
export type ReportRangeCode = (typeof REPORT_RANGE_CODES)[number];

// Giá trị của `granularity` khớp tham số API. Mỗi giá trị tạo một nhóm thời gian khác nhau.
export const REPORT_GRANULARITY_CODES = ['day', 'week', 'month'] as const;
export type ReportGranularityCode = (typeof REPORT_GRANULARITY_CODES)[number];

export const DEFAULT_REPORT_RANGE: ReportRangeCode = '6m';

export interface ReportRangeOption {
  value: ReportRangeCode;
  label: string;
}

export const REPORT_RANGE_OPTIONS: ReportRangeOption[] = [
  { value: '6m', label: '6 tháng gần nhất' },
  { value: '30d', label: '30 ngày gần nhất' },
  { value: '7d', label: '7 ngày gần nhất' },
  { value: 'this_month', label: 'Tháng này' },
];

export interface ReportGranularityOption {
  value: ReportGranularityCode;
  label: string;
}

export const REPORT_GRANULARITY_OPTIONS: ReportGranularityOption[] = [
  { value: 'day', label: 'Ngày' },
  { value: 'week', label: 'Tuần' },
  { value: 'month', label: 'Tháng' },
];

// Số tháng của danh sách chọn: tháng này và 12 tháng trước. Đủ cho khoảng 6 tháng và chọn lùi thêm.
const MONTH_OPTION_COUNT = 13;

// Tháng sớm nhất API báo cáo chấp nhận. Giá trị khớp `ReportRange.EarliestReportDate` của backend.
const EARLIEST_REPORT_MONTH = '2000-01';

const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/;

// Mọi tháng của báo cáo đổi lúc nửa đêm giờ Việt Nam, không theo múi giờ của máy xem.
const vietnamMonthFormatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Ho_Chi_Minh',
  year: 'numeric',
  month: '2-digit',
});

// Tháng hiện tại dạng `YYYY-MM` theo giờ Việt Nam. Hàm nhận `now` để test đặt được thời điểm.
export function currentVietnamMonth(now: Date = new Date()): string {
  const parts = vietnamMonthFormatter.formatToParts(now);
  const year = parts.find((part) => part.type === 'year')?.value ?? '';
  const month = parts.find((part) => part.type === 'month')?.value ?? '';
  return `${year}-${month}`;
}

// Cộng hoặc trừ tháng trên chuỗi `YYYY-MM`, không qua `Date` để không lệch múi giờ.
export function addMonths(month: string, offset: number): string {
  const year = Number(month.slice(0, 4));
  const monthNumber = Number(month.slice(5, 7));
  // Đổi sang số tháng liên tục để cộng qua năm bằng phép chia nguyên.
  const totalMonths = year * 12 + (monthNumber - 1) + offset;
  const nextYear = Math.floor(totalMonths / 12);
  const nextMonth = (totalMonths % 12) + 1;
  return `${nextYear}-${String(nextMonth).padStart(2, '0')}`;
}

export interface ReportMonthOption {
  value: string;
  label: string;
}

// Tháng chọn được, mới nhất trước. Tháng đang chọn cũ hơn danh sách mặc định thì danh sách dài thêm.
export function reportMonthOptions(todayMonth: string, selectedMonth: string): ReportMonthOption[] {
  const defaultEarliest = addMonths(todayMonth, -(MONTH_OPTION_COUNT - 1));
  const earliest = selectedMonth < defaultEarliest ? selectedMonth : defaultEarliest;
  const options: ReportMonthOption[] = [];
  for (let cursor = todayMonth; cursor >= earliest; cursor = addMonths(cursor, -1)) {
    options.push({ value: cursor, label: formatReportMonthTitle(cursor) });
  }
  return options;
}

// Đọc tháng từ URL. Giá trị sai, tháng tương lai hoặc tháng quá cũ dùng tháng hiện tại.
export function readReportMonth(raw: string | null, todayMonth: string): string {
  if (raw === null || !MONTH_PATTERN.test(raw)) return todayMonth;
  // API báo lỗi cho tháng tương lai và tháng trước mốc sớm nhất, nên giao diện không gửi hai loại đó.
  if (raw < EARLIEST_REPORT_MONTH || raw > todayMonth) return todayMonth;
  return raw;
}

// Đọc khoảng thời gian từ URL. Giá trị lạ dùng khoảng mặc định thay vì gửi API và nhận 400.
export function readReportRange(raw: string | null): ReportRangeCode {
  return REPORT_RANGE_CODES.find((code) => code === raw) ?? DEFAULT_REPORT_RANGE;
}

export function isReportRange(value: string): value is ReportRangeCode {
  return REPORT_RANGE_CODES.some((code) => code === value);
}

// Độ chia mặc định của từng khoảng. Sáu tháng chia theo tháng; khoảng ngắn chia theo ngày.
export function defaultGranularityForRange(range: ReportRangeCode): ReportGranularityCode {
  return range === '6m' ? 'month' : 'day';
}

// Đọc độ chia từ URL. Thiếu hoặc sai thì theo mặc định của khoảng đang chọn.
export function readReportGranularity(
  raw: string | null,
  range: ReportRangeCode,
): ReportGranularityCode {
  return REPORT_GRANULARITY_CODES.find((code) => code === raw) ?? defaultGranularityForRange(range);
}

export function isReportGranularity(value: string): value is ReportGranularityCode {
  return REPORT_GRANULARITY_CODES.some((code) => code === value);
}

// Tháng chứa một ngày của nhóm thời gian. Dùng khi bấm một cột để đổi biểu đồ tròn.
export function monthOfReportDate(dateOnly: string): string {
  return dateOnly.slice(0, 7);
}

// Nhãn đầy đủ của một nhóm thời gian, dùng trong bảng và tên truy cập của cột.
export function formatBucketLabel(
  from: string,
  to: string,
  granularity: ReportGranularityCode,
): string {
  if (granularity === 'month') return formatReportMonthTitle(monthOfReportDate(from));
  if (granularity === 'day') return formatReportDate(from);
  return `${formatReportDate(from)} - ${formatReportDate(to)}`;
}

// Nhãn ngắn trên trục hoành. Chia theo tháng hiện thêm hai số cuối của năm để sáu cột vừa màn hẹp.
export function formatBucketTick(from: string, granularity: ReportGranularityCode): string {
  // Bỏ số 0 ở tháng một chữ số để sáu nhãn không chạm nhau trên điện thoại.
  if (granularity === 'month') return `${Number(from.slice(5, 7))}/${from.slice(2, 4)}`;
  return `${from.slice(8, 10)}/${from.slice(5, 7)}`;
}
