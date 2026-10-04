import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import { hashPassword } from '../../../../src/modules/identity/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/seed';
import { generateToken, hashToken } from '../../../../src/modules/identity/tokens';

describe('ID-11 role management integration', () => {
  const app = createApp();
  let adminToken: string;
  let adminUserId: string;

  beforeEach(async () => {
    await seedIdentityRoles(db);

    const adminRole = await db.role.findUniqueOrThrow({ where: { code: 'admin' } });
    const user = await db.user.create({
      data: {
        email: 'admin_roles@pickory.local',
        passwordHash: await hashPassword('AdminPass123!'),
        kind: 'staff',
        roles: { create: { roleId: adminRole.id } },
      },
    });
    adminUserId = user.id;

    adminToken = generateToken();
    await db.session.create({
      data: {
        tokenHash: hashToken(adminToken),
        userId: user.id,
        expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
      },
    });
  });

  it('tạo role mới thành công và ghi đúng 1 dòng audit log', async () => {
    const res = await request(app)
      .post('/api/admin/roles')
      .set('Cookie', `pk_session=${adminToken}`)
      .send({
        code: 'inventory_manager',
        name: 'Quản lý kho',
        permissions: ['products:read', 'inventory:write'],
      });

    expect(res.status).toBe(201);
    expect(res.body.data.code).toBe('inventory_manager');
    expect(res.body.data.permissions).toEqual(['inventory:write', 'products:read']);

    // Kiểm tra có đúng 1 dòng audit log
    const auditLogs = await db.auditLog.findMany({
      where: {
        action: 'role.create',
        targetId: res.body.data.id,
      },
    });
    expect(auditLogs).toHaveLength(1);
    expect(auditLogs[0]?.actorId).toBe(adminUserId);
    expect(auditLogs[0]?.after).toMatchObject({
      code: 'inventory_manager',
      name: 'Quản lý kho',
      permissions: ['inventory:write', 'products:read'],
    });
  });

  it('quyền gửi lên không thuộc danh mục thì nhận mã 400', async () => {
    const res = await request(app)
      .post('/api/admin/roles')
      .set('Cookie', `pk_session=${adminToken}`)
      .send({
        code: 'fake_role',
        name: 'Role Giả Mạo',
        permissions: ['products:read', 'not_a_real_permission'],
      });

    expect(res.status).toBe(400);
    expect(res.body.error.code).toBe('INVALID_PERMISSION');
  });

  it('sửa và xóa role hệ thống bị từ chối với mã 403', async () => {
    const adminRole = await db.role.findUniqueOrThrow({ where: { code: 'admin' } });

    // Sửa role hệ thống -> 403
    const patchRes = await request(app)
      .patch(`/api/admin/roles/${adminRole.id}`)
      .set('Cookie', `pk_session=${adminToken}`)
      .send({ name: 'Tên Mới' });
    expect(patchRes.status).toBe(403);

    // Xóa role hệ thống -> 403
    const deleteRes = await request(app)
      .delete(`/api/admin/roles/${adminRole.id}`)
      .set('Cookie', `pk_session=${adminToken}`);
    expect(deleteRes.status).toBe(403);
  });

  it('xóa role đang có người giữ bị từ chối 400', async () => {
    // Tạo custom role và gán cho 1 user
    const createRes = await request(app)
      .post('/api/admin/roles')
      .set('Cookie', `pk_session=${adminToken}`)
      .send({
        code: 'temporary_role',
        name: 'Role Tạm Thời',
        permissions: ['orders:read'],
      });
    expect(createRes.status).toBe(201);
    const roleId = createRes.body.data.id;

    // Gán role cho 1 staff user
    await db.user.create({
      data: {
        email: 'staff_assigned@pickory.local',
        kind: 'staff',
        roles: { create: { roleId } },
      },
    });

    // Thử xóa role -> 400 ROLE_IN_USE
    const deleteRes = await request(app)
      .delete(`/api/admin/roles/${roleId}`)
      .set('Cookie', `pk_session=${adminToken}`);
    expect(deleteRes.status).toBe(400);
    expect(deleteRes.body.error.code).toBe('ROLE_IN_USE');
  });

  it('chống leo thang đặc quyền: không được tự sửa role mình đang giữ và không được cấp quyền mình không có', async () => {
    // 1. Tạo role custom có roles:manage và orders:read
    const roleA = await db.role.create({
      data: {
        code: 'role_manager',
        name: 'Quản lý Role Giới Hạn',
        permissions: { create: [{ permission: 'roles:manage' }, { permission: 'orders:read' }] },
      },
    });

    // Tạo staff giữ roleA
    const userA = await db.user.create({
      data: {
        email: 'role_mgr_user@pickory.local',
        kind: 'staff',
        roles: { create: [{ roleId: roleA.id }] },
      },
    });

    const mgrToken = generateToken();
    await db.session.create({
      data: {
        tokenHash: hashToken(mgrToken),
        userId: userA.id,
        expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
      },
    });

    // Test 1: User A cố ý PATCH chính role mình đang giữ -> 403 CANNOT_MODIFY_SELF_ROLE
    const patchSelfRes = await request(app)
      .patch(`/api/admin/roles/${roleA.id}`)
      .set('Cookie', `pk_session=${mgrToken}`)
      .send({ permissions: ['roles:manage', 'orders:read', 'staff:manage'] });
    expect(patchSelfRes.status).toBe(403);
    expect(patchSelfRes.body.error.code).toBe('CANNOT_MODIFY_SELF_ROLE');

    // Test 2: User A tạo role mới nhưng cố tình thêm quyền mình KHÔNG có (staff:manage) -> 403 FORBIDDEN
    const createOverPrivRes = await request(app)
      .post('/api/admin/roles')
      .set('Cookie', `pk_session=${mgrToken}`)
      .send({
        code: 'new_escalated_role',
        name: 'Escalated Role',
        permissions: ['orders:read', 'staff:manage'],
      });
    expect(createOverPrivRes.status).toBe(403);
    expect(createOverPrivRes.body.error.code).toBe('FORBIDDEN');
  });

  it('mảng permissions bị trùng được khử trùng và tạo role thành công mà không lỗi P2002', async () => {
    const res = await request(app)
      .post('/api/admin/roles')
      .set('Cookie', `pk_session=${adminToken}`)
      .send({
        code: 'dedup_role',
        name: 'Dedup Role',
        permissions: ['orders:read', 'orders:read', 'orders:read'],
      });
    expect(res.status).toBe(201);
    expect(res.body.data.permissions).toEqual(['orders:read']);
  });
});
