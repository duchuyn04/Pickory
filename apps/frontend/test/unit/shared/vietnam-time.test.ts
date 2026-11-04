import { describe, expect, it } from 'vitest';
import {
  formatVietnamDateTime,
  formatVietnamDayLabel,
  formatVietnamTime,
  isSameVietnamDay,
} from '../../../src/shared/vietnam-time';

describe('formatVietnamDateTime', () => {
  it('hiện giờ Việt Nam (UTC+7)', () => {
    expect(formatVietnamDateTime('2026-10-08T03:30:00Z')).toBe('10:30 08/10/2026');
  });

  it('qua nửa đêm theo giờ Việt Nam sang ngày kế', () => {
    expect(formatVietnamDateTime('2026-10-08T18:05:00Z')).toBe('01:05 09/10/2026');
  });

  it('coi chuỗi thiếu múi giờ là UTC', () => {
    expect(formatVietnamDateTime('2026-10-08T03:30:00')).toBe('10:30 08/10/2026');
  });

  it('chuỗi hỏng trả chuỗi rỗng thay vì ném lỗi', () => {
    expect(formatVietnamDateTime('không-phải-giờ')).toBe('');
  });
});

describe('formatVietnamTime', () => {
  it('hiện giờ phút theo giờ Việt Nam', () => {
    expect(formatVietnamTime('2026-11-03T02:15:05Z')).toBe('09:15');
  });

  it('qua nửa đêm theo giờ Việt Nam sang giờ của ngày kế', () => {
    expect(formatVietnamTime('2026-11-03T18:05:00Z')).toBe('01:05');
  });

  it('coi chuỗi thiếu múi giờ là UTC', () => {
    expect(formatVietnamTime('2026-11-03T02:15:05')).toBe('09:15');
  });
});

describe('formatVietnamDayLabel', () => {
  it('tin cùng ngày Việt Nam với hiện tại là "Hôm nay"', () => {
    const now = new Date('2026-11-04T05:00:00Z');

    expect(formatVietnamDayLabel('2026-11-04T02:00:00Z', now)).toBe('Hôm nay');
  });

  it('tin của ngày trước là "Hôm qua"', () => {
    const now = new Date('2026-11-04T05:00:00Z');

    expect(formatVietnamDayLabel('2026-11-03T10:00:00Z', now)).toBe('Hôm qua');
  });

  it('tin cũ hơn hiện `dd/MM/yyyy`', () => {
    const now = new Date('2026-11-04T05:00:00Z');

    expect(formatVietnamDayLabel('2026-10-30T10:00:00Z', now)).toBe('30/10/2026');
  });

  it('sát nửa đêm theo giờ Việt Nam chia đúng "Hôm qua" và "Hôm nay"', () => {
    const now = new Date('2026-11-03T17:30:00Z');

    expect(formatVietnamDayLabel('2026-11-03T16:59:00Z', now)).toBe('Hôm qua');
    expect(formatVietnamDayLabel('2026-11-03T17:00:00Z', now)).toBe('Hôm nay');
  });
});

describe('isSameVietnamDay', () => {
  it('so sánh hai mốc giờ có cùng ngày theo giờ Việt Nam', () => {
    expect(isSameVietnamDay('2026-11-03T02:00:00Z', '2026-11-03T10:00:00Z')).toBe(true);
    expect(isSameVietnamDay('2026-11-03T16:59:00Z', '2026-11-03T17:00:00Z')).toBe(false);
  });
});
