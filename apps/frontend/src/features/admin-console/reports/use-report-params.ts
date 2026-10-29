import { useListFilters } from '../../../shared/use-list-filters';
import {
  currentVietnamMonth,
  defaultGranularityForRange,
  readReportGranularity,
  readReportMonth,
  readReportRange,
  reportMonthOptions,
  type ReportGranularityCode,
  type ReportMonthOption,
  type ReportRangeCode,
} from './report-period';

// Trang theo kỳ chỉ có khoảng thời gian và độ chia trên URL.
const PERIOD_PARAM_NAMES = ['range', 'granularity'] as const;

export interface MonthReportParams {
  month: string;
  monthOptions: ReportMonthOption[];
  setMonth: (month: string) => void;
  // Nhóm đang chọn trên URL, ví dụ `cod` hoặc mã sản phẩm. Chuỗi rỗng khi không chọn nhóm nào.
  selection: string;
  // Ghi nhóm đang chọn lên URL. Nhấn lại nhóm đang chọn thì bỏ chọn.
  selectGroup: (key: string) => void;
}

// Đọc và ghi tháng cùng nhóm đang chọn của trang biểu đồ tròn. `selectionParamName` là tên tham số riêng
// của trang, như `brand` hay `method`, để mở lại liên kết cho đúng nhóm đang xem.
export function useMonthReportParams(selectionParamName: string): MonthReportParams {
  const paramNames: readonly string[] = ['month', selectionParamName];
  const { values, setFilter } = useListFilters(paramNames);
  const todayMonth = currentVietnamMonth();
  // Tên tham số là chuỗi động, nên bản ghi có thể không có khóa này. Giá trị thiếu đọc thành chuỗi rỗng.
  const month = readReportMonth(values.month ?? null, todayMonth);
  const selection = values[selectionParamName] ?? '';

  return {
    month,
    monthOptions: reportMonthOptions(todayMonth, month),
    // Chỉ ghi tham số của trang này. Tham số khác không bị xóa.
    setMonth: (nextMonth) => setFilter('month', nextMonth),
    selection,
    // Bỏ chọn khi nhấn lại cùng nhóm để bảng chi tiết không mở mãi.
    selectGroup: (key) => setFilter(selectionParamName, selection === key ? '' : key),
  };
}

export interface PeriodReportParams {
  range: ReportRangeCode;
  granularity: ReportGranularityCode;
  setRange: (range: ReportRangeCode) => void;
  setGranularity: (granularity: ReportGranularityCode) => void;
}

// Đọc và ghi khoảng thời gian của trang theo kỳ. Mở lại liên kết cho cùng biểu đồ.
export function usePeriodReportParams(): PeriodReportParams {
  const { values, setFilter } = useListFilters(PERIOD_PARAM_NAMES);
  const range = readReportRange(values.range);
  return {
    range,
    granularity: readReportGranularity(values.granularity, range),
    setRange: (nextRange) => {
      setFilter('range', nextRange);
      // Đổi khoảng thì độ chia về mặc định của khoảng đó, để biểu đồ luôn có nhiều hơn một cột.
      setFilter('granularity', defaultGranularityForRange(nextRange));
    },
    setGranularity: (nextGranularity) => setFilter('granularity', nextGranularity),
  };
}
