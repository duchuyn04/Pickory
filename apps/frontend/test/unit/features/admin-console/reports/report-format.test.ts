import { describe, expect, it } from 'vitest';
import {
  formatCompactVnd,
  formatCount,
  formatPercent,
  formatPercentValue,
  formatReportDate,
  formatReportMonth,
  formatReportMonthTitle,
  formatVnd,
} from '../../../../../src/features/admin-console/reports/report-format';

describe('định dạng tiền VND', () => {
  it('nhóm nghìn theo kiểu Việt Nam và gắn đơn vị', () => {
    expect(formatVnd(1_234_567)).toBe('1.234.567 đ');
    expect(formatVnd(0)).toBe('0 đ');
    expect(formatVnd(999.6)).toBe('1.000 đ');
  });

  it('nhãn trục rút gọn theo triệu và tỷ', () => {
    expect(formatCompactVnd(1_500_000)).toBe('1,5 tr');
    expect(formatCompactVnd(2_000_000_000)).toBe('2 tỷ');
    // Dưới một triệu thì số đầy đủ vẫn ngắn.
    expect(formatCompactVnd(950_000)).toBe('950.000 đ');
  });
});

describe('định dạng số lượt', () => {
  it('nhóm nghìn theo kiểu Việt Nam, không có phần thập phân', () => {
    expect(formatCount(0)).toBe('0');
    expect(formatCount(1_234)).toBe('1.234');
    // Bộ đếm là số nguyên, nên số lẻ chỉ do dữ liệu hỏng và vẫn làm tròn để bảng đọc được.
    expect(formatCount(999.6)).toBe('1.000');
  });
});

describe('định dạng ngày và tháng', () => {
  it('đổi ngày lịch Việt Nam sang dd/MM/yyyy', () => {
    expect(formatReportDate('2026-10-05')).toBe('05/10/2026');
  });

  it('viết tháng theo nhãn câu và nhãn ô chọn', () => {
    expect(formatReportMonth('2026-10')).toBe('tháng 10/2026');
    expect(formatReportMonthTitle('2026-10')).toBe('Tháng 10/2026');
    // Tháng một chữ số bỏ số 0 đầu theo cách viết tiếng Việt.
    expect(formatReportMonthTitle('2026-05')).toBe('Tháng 5/2026');
  });
});

describe('định dạng phần trăm', () => {
  it('làm tròn phần trăm của một lát', () => {
    expect(formatPercent(3, 7)).toBe('43%');
    expect(formatPercent(7, 7)).toBe('100%');
    expect(formatPercent(0, 7)).toBe('0%');
  });

  it('không chia cho 0 khi tổng bằng 0', () => {
    expect(formatPercent(10, 0)).toBe('0%');
  });

  it('lát dưới 1% vẫn hiện dấu nhỏ hơn', () => {
    expect(formatPercentValue(0.005)).toBe('<1%');
    expect(formatPercentValue(0.5)).toBe('50%');
  });
});
