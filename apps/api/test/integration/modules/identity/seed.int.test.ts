import { describe, expect, it } from 'vitest';
import { db } from '../../../../src/shared/db';
import { seedIdentityRoles } from '../../../../src/modules/identity/seed';
import { DEFAULT_ROLE_PERMISSIONS } from '../../../../src/modules/identity/permissions';

describe('identity seed integration', () => {
  it('tạo đủ 4 role với quyền sales và support đúng bảng mặc định', async () => {
    await seedIdentityRoles(db);

    const roles = await db.role.findMany({
      include: { permissions: true },
      orderBy: { code: 'asc' },
    });

    const roleCodes = roles.map((r) => r.code);
    expect(roleCodes).toEqual(['admin', 'customer', 'sales', 'support']);

    const salesRole = roles.find((r) => r.code === 'sales')!;
    const salesPerms = salesRole.permissions.map((p) => p.permission).sort();
    expect(salesPerms).toEqual([...(DEFAULT_ROLE_PERMISSIONS.sales ?? [])].sort());

    const supportRole = roles.find((r) => r.code === 'support')!;
    const supportPerms = supportRole.permissions.map((p) => p.permission).sort();
    expect(supportPerms).toEqual([...(DEFAULT_ROLE_PERMISSIONS.support ?? [])].sort());

    const customerRole = roles.find((r) => r.code === 'customer')!;
    expect(customerRole.permissions).toHaveLength(0);

    const adminRole = roles.find((r) => r.code === 'admin')!;
    expect(adminRole.isSystem).toBe(true);

    // Chạy lại lần 2 kiểm tra tính idempotent
    await seedIdentityRoles(db);
    const count = await db.role.count();
    expect(count).toBe(4);
  });
});
