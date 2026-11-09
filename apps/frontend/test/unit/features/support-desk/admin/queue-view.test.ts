import { describe, expect, it } from 'vitest';
import {
  HANDLED_CALLBACK_LIMIT,
  callbackPhoneHref,
  formatWaitingTime,
  groupCallbackRequests,
  waitingBadgeCount,
} from '../../../../../src/features/support-desk/admin/queue-view';

function row(id: string, status: string) {
  return { id, status };
}

describe('groupCallbackRequests', () => {
  it('đưa dòng pending sang nhóm cần gọi, dù dòng đã xử lý đứng đầu mảng', () => {
    const rows = [
      row('a', 'done'),
      row('b', 'pending'),
      row('c', 'unreachable'),
      row('d', 'pending'),
    ];

    const groups = groupCallbackRequests(rows);

    expect(groups.pending.map((item) => item.id)).toEqual(['b', 'd']);
    expect(groups.handled.map((item) => item.id)).toEqual(['a', 'c']);
    expect(groups.hasHiddenHandled).toBe(false);
  });

  it('trả hai nhóm rỗng khi không có dòng nào', () => {
    expect(groupCallbackRequests([])).toEqual({
      pending: [],
      handled: [],
      hasHiddenHandled: false,
    });
  });

  it('chỉ giữ các dòng đã xử lý đầu tiên theo thứ tự API và báo có dòng bị ẩn', () => {
    const handledRows = Array.from({ length: HANDLED_CALLBACK_LIMIT + 3 }, (_, index) =>
      row(`h${index}`, 'done'),
    );

    const groups = groupCallbackRequests([row('p', 'pending'), ...handledRows]);

    expect(groups.handled).toHaveLength(HANDLED_CALLBACK_LIMIT);
    expect(groups.handled[0]?.id).toBe('h0');
    expect(groups.handled.at(-1)?.id).toBe(`h${HANDLED_CALLBACK_LIMIT - 1}`);
    expect(groups.hasHiddenHandled).toBe(true);
    expect(groups.pending).toHaveLength(1);
  });

  it('đúng 20 dòng đã xử lý thì không báo bị ẩn', () => {
    const handledRows = Array.from({ length: HANDLED_CALLBACK_LIMIT }, (_, index) =>
      row(`h${index}`, 'done'),
    );

    expect(groupCallbackRequests(handledRows).hasHiddenHandled).toBe(false);
  });
});

describe('callbackPhoneHref', () => {
  it.each([
    ['0901234567', 'tel:0901234567'],
    ['0901 234 567', 'tel:0901234567'],
    ['+84 (901) 234-567', 'tel:+84901234567'],
    ['0901.234.567', 'tel:0901234567'],
  ])('chỉ giữ chữ số và dấu +: %s', (phone, expected) => {
    expect(callbackPhoneHref(phone)).toBe(expected);
  });

  it.each([
    ['09-01+23', 'tel:090123'],
    ['+84+901', 'tel:+84901'],
  ])('chỉ giữ dấu + ở đầu số: %s', (phone, expected) => {
    expect(callbackPhoneHref(phone)).toBe(expected);
  });

  it.each([' - . ', '+', '+ -'])('trả null khi không còn chữ số nào: "%s"', (phone) => {
    expect(callbackPhoneHref(phone)).toBeNull();
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
