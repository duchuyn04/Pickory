import { describe, expect, it } from 'vitest';
import {
  formatWaitingTime,
  selectPendingCallbacks,
  waitingBadgeCount,
} from '../../../../../src/features/support-desk/admin/queue-view';

describe('selectPendingCallbacks', () => {
  it('chỉ giữ dòng pending, theo thứ tự ban đầu', () => {
    const rows = [
      { id: 'a', phone: '0900000001', status: 'pending', createdAt: '2026-10-22T01:00:00Z' },
      { id: 'b', phone: '0900000002', status: 'done', createdAt: '2026-10-22T02:00:00Z' },
      { id: 'c', phone: '0900000003', status: 'pending', createdAt: '2026-10-22T03:00:00Z' },
      { id: 'd', phone: '0900000004', status: 'cancelled', createdAt: '2026-10-22T04:00:00Z' },
    ];

    expect(selectPendingCallbacks(rows).map((row) => row.id)).toEqual(['a', 'c']);
  });

  it('trả mảng rỗng khi không có dòng pending', () => {
    const rows = [{ id: 'b', phone: '0900000002', status: 'done', createdAt: 'x' }];

    expect(selectPendingCallbacks(rows)).toEqual([]);
  });
});

describe('waitingBadgeCount', () => {
  it('không có badge khi chưa có dữ liệu', () => {
    expect(waitingBadgeCount(undefined)).toBeNull();
  });

  it('không có badge khi không phiên nào đang chờ', () => {
    expect(waitingBadgeCount([])).toBeNull();
  });

  it('bằng số dòng của danh sách đang chờ', () => {
    const rows = [{ id: '1' }, { id: '2' }, { id: '3' }];

    expect(waitingBadgeCount(rows)).toBe(3);
  });
});

describe('formatWaitingTime', () => {
  it.each([
    [0, 'dưới 1 phút'],
    [59, 'dưới 1 phút'],
    [60, '1 phút'],
    [150, '2 phút'],
    [3599, '59 phút'],
    [3600, '1 giờ'],
    [5400, '1 giờ 30 phút'],
    [7200, '2 giờ'],
  ])('%i giây hiện là "%s"', (seconds, expected) => {
    expect(formatWaitingTime(seconds)).toBe(expected);
  });

  it('coi số âm như 0 giây, vì đồng hồ máy khách có thể chạy lệch', () => {
    expect(formatWaitingTime(-30)).toBe('dưới 1 phút');
  });
});
