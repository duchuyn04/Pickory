import { describe, expect, it } from 'vitest';
import {
  applyPresenceChange,
  isStaffAvailable,
} from '../../../../../src/features/support-desk/admin/presence-state';

const NOW = '2026-10-22T08:00:00.000Z';

describe('isStaffAvailable', () => {
  it('đúng khi nhân viên có trong danh sách đang sẵn sàng', () => {
    const list = [{ staffId: 'staff-1', status: 'available' as const, lastSeenAt: NOW }];

    expect(isStaffAvailable(list, 'staff-1')).toBe(true);
  });

  it('sai khi nhân viên không có trong danh sách', () => {
    const list = [{ staffId: 'staff-2', status: 'available' as const, lastSeenAt: NOW }];

    expect(isStaffAvailable(list, 'staff-1')).toBe(false);
  });

  it('sai khi chưa có danh sách hoặc chưa biết mã nhân viên', () => {
    expect(isStaffAvailable(undefined, 'staff-1')).toBe(false);
    expect(isStaffAvailable([], null)).toBe(false);
  });
});

describe('applyPresenceChange', () => {
  it('thêm nhân viên vào danh sách khi chuyển sang available', () => {
    const result = applyPresenceChange([], { staffId: 'staff-1', status: 'available' }, NOW);

    expect(result).toEqual([{ staffId: 'staff-1', status: 'available', lastSeenAt: NOW }]);
  });

  it('bỏ nhân viên khỏi danh sách khi chuyển sang away', () => {
    const list = [
      { staffId: 'staff-1', status: 'available' as const, lastSeenAt: NOW },
      { staffId: 'staff-2', status: 'available' as const, lastSeenAt: NOW },
    ];

    const result = applyPresenceChange(list, { staffId: 'staff-1', status: 'away' }, NOW);

    expect(result.map((entry) => entry.staffId)).toEqual(['staff-2']);
  });

  it('không nhân đôi dòng khi nhận available hai lần', () => {
    const list = [
      { staffId: 'staff-1', status: 'available' as const, lastSeenAt: '2026-10-22T07:00:00Z' },
    ];

    const result = applyPresenceChange(list, { staffId: 'staff-1', status: 'available' }, NOW);

    expect(result).toEqual([{ staffId: 'staff-1', status: 'available', lastSeenAt: NOW }]);
  });

  it('không sửa danh sách đầu vào', () => {
    const list = [{ staffId: 'staff-1', status: 'available' as const, lastSeenAt: NOW }];

    applyPresenceChange(list, { staffId: 'staff-1', status: 'away' }, NOW);

    expect(list).toHaveLength(1);
  });
});
