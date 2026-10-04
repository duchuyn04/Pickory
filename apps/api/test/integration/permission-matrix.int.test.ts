import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../src/app';
import { hashPassword } from '../../src/modules/identity/password';
import { DEFAULT_ROLE_PERMISSIONS, type Permission } from '../../src/modules/identity/permissions';
import { seedIdentityRoles } from '../../src/modules/identity/seed';
import { generateToken, hashToken } from '../../src/modules/identity/tokens';
import { db } from '../../src/shared/db';

interface EndpointPermissionRow {
  method: 'get' | 'post' | 'put' | 'patch' | 'delete';
  path: string;
  requiredPermissions: Permission[];
}

export const BACKOFFICE_ENDPOINTS: EndpointPermissionRow[] = [
  {
    method: 'get',
    path: '/api/admin/audit-logs',
    requiredPermissions: ['audit-logs:read'],
  },
  {
    method: 'get',
    path: '/api/admin/permissions',
    requiredPermissions: ['roles:manage'],
  },
  {
    method: 'get',
    path: '/api/admin/roles',
    requiredPermissions: ['roles:manage'],
  },
  {
    method: 'post',
    path: '/api/admin/roles',
    requiredPermissions: ['roles:manage'],
  },
  {
    method: 'patch',
    path: '/api/admin/roles/dummy-id',
    requiredPermissions: ['roles:manage'],
  },
  {
    method: 'delete',
    path: '/api/admin/roles/dummy-id',
    requiredPermissions: ['roles:manage'],
  },
  {
    method: 'get',
    path: '/api/admin/staff',
    requiredPermissions: ['staff:read'],
  },
  {
    method: 'post',
    path: '/api/admin/staff/invitations',
    requiredPermissions: ['staff:manage'],
  },
  {
    method: 'put',
    path: '/api/admin/staff/dummy-id/roles',
    requiredPermissions: ['staff:manage'],
  },
  {
    method: 'post',
    path: '/api/admin/staff/dummy-id/lock',
    requiredPermissions: ['staff:manage'],
  },
  {
    method: 'post',
    path: '/api/admin/staff/dummy-id/unlock',
    requiredPermissions: ['staff:manage'],
  },
  {
    method: 'get',
    path: '/api/admin/customers',
    requiredPermissions: ['customers:read'],
  },
  {
    method: 'get',
    path: '/api/admin/customers/dummy-id',
    requiredPermissions: ['customers:read'],
  },
  {
    method: 'post',
    path: '/api/admin/customers/dummy-id/lock',
    requiredPermissions: ['customers:manage'],
  },
  {
    method: 'post',
    path: '/api/admin/customers/dummy-id/unlock',
    requiredPermissions: ['customers:manage'],
  },
];

describe('ID-04: Khung test ma trận quyền back-office', () => {
  const app = createApp();

  beforeEach(async () => {
    await seedIdentityRoles(db);
  });

  async function createUserSession(email: string, kind: 'customer' | 'staff', roleCodes: string[]) {
    const roles = await db.role.findMany({
      where: { code: { in: roleCodes } },
    });
    const passwordHash = await hashPassword('Pass123456!');
    const user = await db.user.create({
      data: {
        email,
        emailVerified: true,
        passwordHash,
        kind,
        roles: {
          create: roles.map((r) => ({ roleId: r.id })),
        },
      },
    });

    const token = generateToken();
    const tokenHash = hashToken(token);
    await db.session.create({
      data: {
        tokenHash,
        userId: user.id,
        expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
      },
    });

    return token;
  }

  for (const endpoint of BACKOFFICE_ENDPOINTS) {
    describe(`${endpoint.method.toUpperCase()} ${endpoint.path}`, () => {
      it('chưa đăng nhập (guest) nhận 401', async () => {
        const res = await request(app)[endpoint.method](endpoint.path);
        expect(res.status).toBe(401);
      });

      it('khách hàng (customer) nhận 403', async () => {
        const token = await createUserSession('cust_matrix@pickory.local', 'customer', [
          'customer',
        ]);
        const res = await request(app)
          [endpoint.method](endpoint.path)
          .set('Cookie', `pk_session=${token}`);
        expect(res.status).toBe(403);
      });

      it('nhân viên sales chỉ gọi được khi có quyền trong danh mục, nếu thiếu nhận 403', async () => {
        const token = await createUserSession('sales_matrix@pickory.local', 'staff', ['sales']);
        const res = await request(app)
          [endpoint.method](endpoint.path)
          .set('Cookie', `pk_session=${token}`);
        const salesPerms = new Set(DEFAULT_ROLE_PERMISSIONS.sales);
        const hasAccess = endpoint.requiredPermissions.every((p) => salesPerms.has(p));
        if (hasAccess) {
          expect(res.status).not.toBe(401);
          expect(res.status).not.toBe(403);
        } else {
          expect(res.status).toBe(403);
        }
      });

      it('nhân viên support chỉ gọi được khi có quyền trong danh mục, nếu thiếu nhận 403', async () => {
        const token = await createUserSession('support_matrix@pickory.local', 'staff', ['support']);
        const res = await request(app)
          [endpoint.method](endpoint.path)
          .set('Cookie', `pk_session=${token}`);
        const supportPerms = new Set(DEFAULT_ROLE_PERMISSIONS.support);
        const hasAccess = endpoint.requiredPermissions.every((p) => supportPerms.has(p));
        if (hasAccess) {
          expect(res.status).not.toBe(401);
          expect(res.status).not.toBe(403);
        } else {
          expect(res.status).toBe(403);
        }
      });

      it('admin có mọi quyền ngầm định và gọi thành công (200)', async () => {
        const token = await createUserSession('admin_matrix@pickory.local', 'staff', ['admin']);
        const res = await request(app)
          [endpoint.method](endpoint.path)
          .set('Cookie', `pk_session=${token}`);
        expect(res.status).not.toBe(401);
        expect(res.status).not.toBe(403);
      });
    });
  }
});
