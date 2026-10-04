import { describe, expect, it, vi } from 'vitest';
import {
  DEFAULT_ROLE_PERMISSIONS,
  mergePermissions,
  PERMISSIONS,
  type Permission,
} from '../../../../src/modules/identity/permissions';
import { logger } from '../../../../src/shared/logger';

describe('identity permissions', () => {
  it('hợp quyền của nhân viên giữ sales + support đúng và đủ', () => {
    const rawSalesAndSupport = [
      ...(DEFAULT_ROLE_PERMISSIONS.sales ?? []),
      ...(DEFAULT_ROLE_PERMISSIONS.support ?? []),
    ];
    const merged = mergePermissions(['sales', 'support'], rawSalesAndSupport);

    // Bảng hợp quyền kỳ vọng
    const expected = new Set<Permission>([
      'customers:read',
      'products:read',
      'orders:read',
      'orders:update-status',
      'orders:cancel',
      'orders:fulfill',
      'payments:read',
      'payments:reconcile',
      'reviews:moderate',
      'conversations:read',
      'conversations:reply',
      'knowledge:read',
      'ai-usage:read',
      'reports:read',
    ]);

    expect(merged.size).toBe(expected.size);
    for (const p of expected) {
      expect(merged.has(p)).toBe(true);
    }
  });

  it('admin luôn có mọi quyền trong danh mục ngầm định mà không cần seed', () => {
    const merged = mergePermissions(['admin'], []);
    expect(merged.size).toBe(PERMISSIONS.length);
    for (const p of PERMISSIONS) {
      expect(merged.has(p)).toBe(true);
    }
  });

  it('bỏ qua quyền lạ trong role_permissions và ghi cảnh báo qua logger', () => {
    const warnSpy = vi.spyOn(logger, 'warn').mockImplementation(() => logger);
    const merged = mergePermissions(
      ['sales'],
      ['orders:read', 'unknown:permission_xyz', 'another:fake_permission'],
    );

    expect(merged.has('orders:read')).toBe(true);
    expect(merged.has('unknown:permission_xyz' as Permission)).toBe(false);
    expect(warnSpy).toHaveBeenCalledWith(
      expect.objectContaining({ permission: 'unknown:permission_xyz' }),
      expect.any(String),
    );
    warnSpy.mockRestore();
  });
});
