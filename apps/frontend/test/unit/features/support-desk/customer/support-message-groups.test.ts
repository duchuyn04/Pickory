import { describe, expect, it } from 'vitest';
import { groupSupportMessages } from '../../../../../src/features/support-desk/customer/support-message-groups';
import type { SupportMessage } from '../../../../../src/features/support-desk/customer/support-customer-schemas';

function message(partial: Partial<SupportMessage> & { id: string }): SupportMessage {
  return {
    role: 'customer',
    body: 'Xin chào',
    action: null,
    staffDisplayName: null,
    createdAt: '2026-11-03T02:00:00Z',
    ...partial,
  };
}

describe('groupSupportMessages', () => {
  it('tin đầu hội thoại mở nhóm', () => {
    const items = groupSupportMessages([message({ id: 'm1' })]);

    expect(items).toEqual([{ message: expect.objectContaining({ id: 'm1' }), startsGroup: true }]);
  });

  it('tin liên tiếp cùng người gửi trong 5 phút nằm cùng nhóm', () => {
    const items = groupSupportMessages([
      message({ id: 'm1', createdAt: '2026-11-03T02:00:00Z' }),
      message({ id: 'm2', createdAt: '2026-11-03T02:02:00Z' }),
      message({ id: 'm3', createdAt: '2026-11-03T02:05:00Z' }),
    ]);

    expect(items.map((item) => item.startsGroup)).toEqual([true, false, false]);
  });

  it('cùng người gửi nhưng cách quá 5 phút thì mở nhóm mới', () => {
    const items = groupSupportMessages([
      message({ id: 'm1', createdAt: '2026-11-03T02:00:00Z' }),
      message({ id: 'm2', createdAt: '2026-11-03T02:05:01Z' }),
    ]);

    expect(items.map((item) => item.startsGroup)).toEqual([true, true]);
  });

  it('đổi người gửi thì mở nhóm mới', () => {
    const items = groupSupportMessages([
      message({ id: 'm1', createdAt: '2026-11-03T02:00:00Z' }),
      message({
        id: 'm2',
        role: 'staff',
        staffDisplayName: 'Lan - CSKH',
        createdAt: '2026-11-03T02:01:00Z',
      }),
      message({ id: 'm3', createdAt: '2026-11-03T02:02:00Z' }),
    ]);

    expect(items.map((item) => item.startsGroup)).toEqual([true, true, true]);
  });

  it('nhân viên có tên và không tên tách nhóm, vì tên hiện khác nhau', () => {
    const items = groupSupportMessages([
      message({ id: 'm1', role: 'staff', staffDisplayName: 'Lan - CSKH' }),
      message({ id: 'm2', role: 'staff', staffDisplayName: null }),
    ]);

    expect(items.map((item) => item.startsGroup)).toEqual([true, true]);
  });

  it('cách tin đầu nhóm quá 5 phút thì mở nhóm mới dù cách tin liền trước dưới 5 phút', () => {
    const items = groupSupportMessages([
      message({ id: 'm1', createdAt: '2026-11-03T02:00:00Z' }),
      message({ id: 'm2', createdAt: '2026-11-03T02:04:00Z' }),
      message({ id: 'm3', createdAt: '2026-11-03T02:08:00Z' }),
    ]);

    expect(items.map((item) => item.startsGroup)).toEqual([true, false, true]);
  });

  it('sang ngày mới theo giờ Việt Nam thì mở nhóm mới dù cách dưới 5 phút', () => {
    // 23:58 ngày 03/11 và 00:01 ngày 04/11 theo giờ Việt Nam (UTC+7).
    const items = groupSupportMessages([
      message({ id: 'm1', createdAt: '2026-11-03T16:58:00Z' }),
      message({ id: 'm2', createdAt: '2026-11-03T17:01:00Z' }),
    ]);

    expect(items.map((item) => item.startsGroup)).toEqual([true, true]);
  });
});
