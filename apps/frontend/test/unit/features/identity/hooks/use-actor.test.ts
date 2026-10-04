import { describe, expect, it } from 'vitest';
import {
  type ActorData,
  checkPermission,
} from '../../../../../src/features/identity/hooks/use-actor';

describe('UI-05 checkPermission helper', () => {
  it('trả về false khi actor rỗng hoặc null', () => {
    expect(checkPermission(null, 'orders:read')).toBe(false);
    expect(checkPermission(undefined, 'orders:read')).toBe(false);
  });

  it('admin luôn có mọi quyền', () => {
    const adminActor: ActorData = {
      anonymousId: 'anon-1',
      userId: 'admin-id',
      kind: 'staff',
      roles: ['admin'],
      permissions: [],
      consent: { personalization: true, marketing: true },
    };
    expect(checkPermission(adminActor, 'orders:read')).toBe(true);
    expect(checkPermission(adminActor, 'roles:manage')).toBe(true);
    expect(checkPermission(adminActor, 'unknown:future_perm')).toBe(true);
  });

  it('nhân viên thường chỉ có các quyền trong mảng permissions', () => {
    const salesActor: ActorData = {
      anonymousId: 'anon-2',
      userId: 'sales-id',
      kind: 'staff',
      roles: ['sales'],
      permissions: ['orders:read', 'orders:update-status'],
      consent: { personalization: false, marketing: false },
    };
    expect(checkPermission(salesActor, 'orders:read')).toBe(true);
    expect(checkPermission(salesActor, 'orders:update-status')).toBe(true);
    expect(checkPermission(salesActor, 'roles:manage')).toBe(false);
  });
});
