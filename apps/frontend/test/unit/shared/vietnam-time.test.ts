import { describe, expect, it } from 'vitest';
import { formatVietnamDateTime } from '../../../src/shared/vietnam-time';

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
});
