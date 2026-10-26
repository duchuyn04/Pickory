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

// Trang thương hiệu chỉ có tham số tháng trên URL.
const BRAND_PARAM_NAMES = ['month'] as const;
// Trang theo kỳ chỉ có khoảng thời gian và độ chia trên URL.
const PERIOD_PARAM_NAMES = ['range', 'granularity'] as const;

export interface BrandReportParams {
  month: string;
  monthOptions: ReportMonthOption[];
  setMonth: (month: string) => void;
}

// Đọc và ghi tháng của trang thương hiệu. Mở lại liên kết cho đúng tháng.
export function useBrandReportParams(): BrandReportParams {
  const { values, setFilter } = useListFilters(BRAND_PARAM_NAMES);
  const todayMonth = currentVietnamMonth();
  const month = readReportMonth(values.month, todayMonth);
  return {
    month,
    monthOptions: reportMonthOptions(todayMonth, month),
    // Chỉ ghi tham số của trang này. Tham số khác không bị xóa.
    setMonth: (nextMonth) => setFilter('month', nextMonth),
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
