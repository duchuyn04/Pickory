import { describe, expect, it } from 'vitest';
import {
  addMonths,
  currentVietnamMonth,
  defaultGranularityForRange,
  formatBucketLabel,
  formatBucketTick,
  readReportGranularity,
  readReportMonth,
  readReportRange,
  reportMonthOptions,
} from '../../../../../src/features/admin-console/reports/report-period';

describe('tháng theo giờ Việt Nam', () => {
  it('đổi tháng lúc nửa đêm giờ Việt Nam, không theo giờ UTC', () => {
    // 23:59 ngày 31/10 giờ Việt Nam vẫn là tháng 10, dù UTC đã sang ngày 31.
    expect(currentVietnamMonth(new Date('2026-10-31T16:59:00Z'))).toBe('2026-10');
    // 00:00 ngày 01/11 giờ Việt Nam là 17:00 UTC ngày 31/10.
    expect(currentVietnamMonth(new Date('2026-10-31T17:00:00Z'))).toBe('2026-11');
  });

  it('cộng trừ tháng qua năm', () => {
    expect(addMonths('2026-01', -1)).toBe('2025-12');
    expect(addMonths('2026-12', 1)).toBe('2027-01');
    expect(addMonths('2026-10', -12)).toBe('2025-10');
  });

  it('danh sách chọn tháng có 13 tháng, mới nhất trước', () => {
    const options = reportMonthOptions('2026-10', '2026-10');

    expect(options).toHaveLength(13);
    expect(options[0]?.value).toBe('2026-10');
    expect(options[12]?.value).toBe('2025-10');
    expect(options[0]?.label).toBe('Tháng 10/2026');
  });

  it('danh sách dài thêm khi URL trỏ tới tháng cũ hơn mặc định', () => {
    const options = reportMonthOptions('2026-10', '2024-01');

    expect(options[options.length - 1]?.value).toBe('2024-01');
  });
});

describe('đọc tham số thời gian từ URL', () => {
  it('giữ tháng hợp lệ và thay tháng sai bằng tháng hiện tại', () => {
    expect(readReportMonth('2026-09', '2026-10')).toBe('2026-09');
    expect(readReportMonth(null, '2026-10')).toBe('2026-10');
    expect(readReportMonth('2026-9', '2026-10')).toBe('2026-10');
    expect(readReportMonth('2026-13', '2026-10')).toBe('2026-10');
    // API từ chối tháng tương lai và tháng trước mốc sớm nhất.
    expect(readReportMonth('2026-11', '2026-10')).toBe('2026-10');
    expect(readReportMonth('1999-12', '2026-10')).toBe('2026-10');
  });

  it('khoảng thời gian mặc định là 6 tháng gần nhất', () => {
    expect(readReportRange(null)).toBe('6m');
    expect(readReportRange('30d')).toBe('30d');
    // `month` là khoảng riêng của biểu đồ tròn, không thuộc bộ chọn của biểu đồ cột.
    expect(readReportRange('month')).toBe('6m');
  });

  it('độ chia thiếu hoặc sai theo mặc định của khoảng', () => {
    expect(defaultGranularityForRange('6m')).toBe('month');
    expect(defaultGranularityForRange('30d')).toBe('day');
    expect(readReportGranularity(null, '6m')).toBe('month');
    expect(readReportGranularity(null, '7d')).toBe('day');
    expect(readReportGranularity('week', '6m')).toBe('week');
    expect(readReportGranularity('year', 'this_month')).toBe('day');
  });
});

describe('nhãn của nhóm thời gian', () => {
  it('nhãn đầy đủ theo độ chia', () => {
    expect(formatBucketLabel('2026-10-01', '2026-10-31', 'month')).toBe('Tháng 10/2026');
    expect(formatBucketLabel('2026-10-26', '2026-10-26', 'day')).toBe('26/10/2026');
    expect(formatBucketLabel('2026-10-20', '2026-10-26', 'week')).toBe('20/10/2026 - 26/10/2026');
  });

  it('nhãn trục ngắn cho màn hẹp', () => {
    expect(formatBucketTick('2026-10-01', 'month')).toBe('10/26');
    expect(formatBucketTick('2026-10-05', 'day')).toBe('05/10');
    expect(formatBucketTick('2026-10-20', 'week')).toBe('20/10');
  });
});
