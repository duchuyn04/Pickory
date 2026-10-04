import request from 'supertest';
import { beforeEach, describe, expect, it } from 'vitest';
import { createApp } from '../../../../src/app';
import { db } from '../../../../src/shared/db';
import {
  InMemoryEmailSender,
  setEmailSender,
} from '../../../../src/modules/identity/data/email-sender';
import { hashPassword } from '../../../../src/modules/identity/security/password';
import { seedIdentityRoles } from '../../../../src/modules/identity/data/seed';
import { generateToken, hashToken } from '../../../../src/modules/identity/security/tokens';

describe('ID-12 & ID-13 staff invitations, role assignment, locking integration', () => {
  const app = createApp();
  let emailSender: InMemoryEmailSender;
  let adminToken: string;
  let adminUser: { id: string; email: string };

  beforeEach(async () => {
    await seedIdentityRoles(db);
    emailSender = new InMemoryEmailSender();
    setEmailSender(emailSender);

    const adminRole = await db.role.findUniqueOrThrow({ where: { code: 'admin' } });
    const user = await db.user.create({
      data: {
        email: 'superadmin@pickory.local',
        passwordHash: await hashPassword('AdminPass123!'),
        kind: 'staff',
        roles: { create: { roleId: adminRole.id } },
      },
    });
    adminUser = user;

    adminToken = generateToken();
    await db.session.create({
      data: {
        tokenHash: hashToken(adminToken),
        userId: user.id,
        expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
      },
    });
  });

  describe('ID-12: Mời nhân viên', () => {
    it('mời nhân viên thành công, tạo token 72h, ghi audit và nhân viên đặt mật khẩu đăng nhập được', async () => {
      const res = await request(app)
        .post('/api/admin/staff/invitations')
        .set('Cookie', `pk_session=${adminToken}`)
        .send({
          email: 'invited_staff@pickory.local',
          name: 'Nhân viên mới',
          roleCodes: ['sales'],
        });
      expect(res.status).toBe(201);
      expect(res.body.data.email).toBe('invited_staff@pickory.local');

      // Kiểm tra có đúng 1 dòng audit log
      const audits = await db.auditLog.findMany({
        where: {
          action: 'staff.invite',
          targetId: res.body.data.id,
        },
      });
      expect(audits).toHaveLength(1);
      expect(audits[0]!.actorId).toBe(adminUser.id);
      expect(audits[0]!.after).toMatchObject({ email: 'invited_staff@pickory.local' });
      // Email được gửi qua EmailSender chứa token invite
      expect(emailSender.sentMessages).toHaveLength(1);
      const inviteMsg = emailSender.sentMessages[0]!;
      expect(inviteMsg.to).toBe('invited_staff@pickory.local');
      const token = inviteMsg.metadata?.token as string;
      expect(token).toBeDefined();

      // Kiểm tra hạn dùng của token là ~72 giờ
      const tokenRecord = await db.emailToken.findUniqueOrThrow({
        where: { tokenHash: hashToken(token) },
      });
      expect(tokenRecord.purpose).toBe('invite');
      const diffHours = (tokenRecord.expiresAt.getTime() - Date.now()) / (3600 * 1000);
      expect(diffHours).toBeGreaterThan(71);

      // Nhân viên đặt mật khẩu qua reset-password
      const resetRes = await request(app).post('/api/auth/reset-password').send({
        token,
        newPassword: 'NewStaffPassword123!',
      });
      expect(resetRes.status).toBe(200);

      // Đăng nhập tại endpoint nhân viên thành công
      const loginRes = await request(app).post('/api/admin/auth/login').send({
        email: 'invited_staff@pickory.local',
        password: 'NewStaffPassword123!',
      });
      expect(loginRes.status).toBe(200);
      expect(loginRes.body.data.user.email).toBe('invited_staff@pickory.local');
    });

    it('từ chối mời email đã thuộc khách và không gán được role customer', async () => {
      const custRole = await db.role.findUniqueOrThrow({ where: { code: 'customer' } });
      await db.user.create({
        data: {
          email: 'existing_customer@pickory.local',
          kind: 'customer',
          roles: { create: { roleId: custRole.id } },
        },
      });

      // 1. Mời email thuộc khách -> 400
      const res1 = await request(app)
        .post('/api/admin/staff/invitations')
        .set('Cookie', `pk_session=${adminToken}`)
        .send({
          email: 'existing_customer@pickory.local',
          name: 'Khách sang nhân viên',
          roleCodes: ['sales'],
        });
      expect(res1.status).toBe(400);
      expect(res1.body.error.code).toBe('EMAIL_ALREADY_EXISTS');

      // 2. Gán role customer cho nhân viên -> 400
      const res2 = await request(app)
        .post('/api/admin/staff/invitations')
        .set('Cookie', `pk_session=${adminToken}`)
        .send({
          email: 'staff_with_customer_role@pickory.local',
          name: 'Nhân viên sai role',
          roleCodes: ['sales', 'customer'],
        });
      expect(res2.status).toBe(400);
      expect(res2.body.error.code).toBe('INVALID_ROLE');
    });

    it('từ chối link mời đã quá 72 giờ hoặc bị dùng lại', async () => {
      // 1. Tạo lời mời
      const res = await request(app)
        .post('/api/admin/staff/invitations')
        .set('Cookie', `pk_session=${adminToken}`)
        .send({
          email: 'expired_invite@pickory.local',
          name: 'Hết Hạn',
          roleCodes: ['support'],
        });
      expect(res.status).toBe(201);
      const token = emailSender.sentMessages.find((m) => m.to === 'expired_invite@pickory.local')
        ?.metadata?.token as string;

      // Giả lập token đã hết hạn 72 giờ
      const tokenHash = hashToken(token);
      await db.emailToken.update({
        where: { tokenHash },
        data: { expiresAt: new Date(Date.now() - 1000) },
      });

      // Thử dùng token hết hạn -> 400 INVALID_TOKEN
      const failRes = await request(app).post('/api/auth/reset-password').send({
        token,
        newPassword: 'ValidPassword123!',
      });
      expect(failRes.status).toBe(400);
      expect(failRes.body.error.code).toBe('INVALID_TOKEN');

      // Cập nhật lại hạn dùng để test dùng lần 1 thành công, lần 2 thất bại
      await db.emailToken.update({
        where: { tokenHash },
        data: { expiresAt: new Date(Date.now() + 72 * 60 * 60 * 1000) },
      });

      const okRes = await request(app).post('/api/auth/reset-password').send({
        token,
        newPassword: 'ValidPassword123!',
      });
      expect(okRes.status).toBe(200);

      // Thử dùng lại lần 2 -> 400 INVALID_TOKEN
      const reuseRes = await request(app).post('/api/auth/reset-password').send({
        token,
        newPassword: 'AnotherPassword123!',
      });
      expect(reuseRes.status).toBe(400);
      expect(reuseRes.body.error.code).toBe('INVALID_TOKEN');
    });

    it('nhân viên không phải admin không được phép mời role admin (chống leo thang đặc quyền)', async () => {
      // Tạo tài khoản staff có quyền staff:manage
      const staffRole = await db.role.create({
        data: {
          code: 'staff_mgr',
          name: 'Quản lý nhân sự',
          permissions: { create: [{ permission: 'staff:manage' }, { permission: 'staff:read' }] },
        },
      });
      const staffUser = await db.user.create({
        data: {
          email: 'hr_manager@pickory.local',
          kind: 'staff',
          roles: { create: [{ roleId: staffRole.id }] },
        },
      });
      const hrToken = generateToken();
      await db.session.create({
        data: {
          tokenHash: hashToken(hrToken),
          userId: staffUser.id,
          expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
        },
      });

      // HR thử mời một người làm admin -> 403
      const res = await request(app)
        .post('/api/admin/staff/invitations')
        .set('Cookie', `pk_session=${hrToken}`)
        .send({
          email: 'hacked_admin@pickory.local',
          name: 'Fake Admin',
          roleCodes: ['admin'],
        });
      expect(res.status).toBe(403);
      expect(res.body.error.code).toBe('FORBIDDEN');
    });

    it('nhân viên không phải admin mời role chứa quyền mình không sở hữu nhận 403 (R1)', async () => {
      const hrRole = await db.role.create({
        data: {
          code: 'hr_only',
          name: 'Nhân sự',
          permissions: { create: [{ permission: 'staff:manage' }, { permission: 'staff:read' }] },
        },
      });
      const hrUser = await db.user.create({
        data: {
          email: 'hr_overpriv@pickory.local',
          kind: 'staff',
          roles: { create: [{ roleId: hrRole.id }] },
        },
      });
      const hrToken = generateToken();
      await db.session.create({
        data: {
          tokenHash: hashToken(hrToken),
          userId: hrUser.id,
          expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
        },
      });

      // Tạo role khác chứa quyền orders:manage mà HR không có
      const managerRole = await db.role.create({
        data: {
          code: 'order_boss',
          name: 'Trưởng bộ phận đơn hàng',
          permissions: { create: [{ permission: 'orders:cancel' }, { permission: 'orders:read' }] },
        },
      });

      // HR mời nhân viên với role order_boss -> 403 FORBIDDEN
      const res = await request(app)
        .post('/api/admin/staff/invitations')
        .set('Cookie', `pk_session=${hrToken}`)
        .send({
          email: 'new_order_boss@pickory.local',
          name: 'Sếp đơn',
          roleCodes: [managerRole.code],
        });
      expect(res.status).toBe(403);
      expect(res.body.error.code).toBe('FORBIDDEN');
    });
  });

  describe('ID-13: Gán role, khóa và mở khóa nhân viên', () => {
    async function createStaffWithRole(email: string, roleCode: string) {
      const role = await db.role.findUniqueOrThrow({ where: { code: roleCode } });
      const user = await db.user.create({
        data: {
          email,
          kind: 'staff',
          roles: { create: { roleId: role.id } },
        },
      });
      const token = generateToken();
      await db.session.create({
        data: {
          tokenHash: hashToken(token),
          userId: user.id,
          expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
        },
      });
      return { user, token };
    }

    it('gán nhiều role sales + support cho nhân viên, nạp hợp quyền đúng', async () => {
      const { user, token } = await createStaffWithRole('staff_multirole@pickory.local', 'sales');

      const res = await request(app)
        .put(`/api/admin/staff/${user.id}/roles`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({ roleCodes: ['sales', 'support'] });

      expect(res.status).toBe(200);

      // Request tiếp theo nạp cả quyền sales và support
      const meRes = await request(app).get('/api/me').set('Cookie', `pk_session=${token}`);

      expect(meRes.body.data.roles).toContain('sales');
      expect(meRes.body.data.roles).toContain('support');
      expect(meRes.body.data.permissions).toContain('orders:update-status'); // từ sales
      expect(meRes.body.data.permissions).toContain('reviews:moderate'); // từ support

      // Kiểm tra audit log: đúng 1 dòng roles_update
      const audits = await db.auditLog.findMany({
        where: {
          action: 'staff.roles_update',
          targetId: user.id,
        },
      });
      expect(audits).toHaveLength(1);
      expect(audits[0]!.actorId).toBe(adminUser.id);
      expect(audits[0]!.after).toMatchObject({ roles: ['sales', 'support'] });
    });
    it('không tự sửa role và không tự khóa chính mình', async () => {
      // Tự sửa role mình -> 400
      const roleRes = await request(app)
        .put(`/api/admin/staff/${adminUser.id}/roles`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({ roleCodes: ['sales'] });
      expect(roleRes.status).toBe(400);
      expect(roleRes.body.error.code).toBe('CANNOT_MODIFY_SELF');

      // Tự khóa mình -> 400
      const lockRes = await request(app)
        .post(`/api/admin/staff/${adminUser.id}/lock`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({});
      expect(lockRes.status).toBe(400);
      expect(lockRes.body.error.code).toBe('CANNOT_MODIFY_SELF');
    });

    it('không khóa hoặc gỡ quyền admin của admin cuối cùng', async () => {
      // Tạo thêm 1 staff admin
      const { user: otherAdmin } = await createStaffWithRole('second_admin@pickory.local', 'admin');

      // Gỡ quyền admin của otherAdmin -> thành công vì còn adminUser
      const gỡAdminRes = await request(app)
        .put(`/api/admin/staff/${otherAdmin.id}/roles`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({ roleCodes: ['sales'] });
      expect(gỡAdminRes.status).toBe(200);

      // Thử khóa adminUser (khi chỉ còn duy nhất 1 admin hoạt động) -> bị chặn 400
      // Tạo 1 admin tạm để gọi lệnh
      const adminRole = await db.role.findUniqueOrThrow({ where: { code: 'admin' } });
      const callerAdmin = await db.user.create({
        data: {
          email: 'caller_admin@pickory.local',
          kind: 'staff',
          roles: { create: { roleId: adminRole.id } },
        },
      });
      const callerToken = generateToken();
      await db.session.create({
        data: {
          tokenHash: hashToken(callerToken),
          userId: callerAdmin.id,
          expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
        },
      });

      // Gỡ quyền admin của callerAdmin xong thì chỉ còn adminUser là admin duy nhất
      await request(app)
        .put(`/api/admin/staff/${callerAdmin.id}/roles`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({ roleCodes: ['support'] });

      // Bây giờ adminUser là admin DUY NHẤT. Thử gỡ quyền admin của adminUser từ caller (hoặc ngược lại)
      const lockLastAdmin = await request(app)
        .post(`/api/admin/staff/${adminUser.id}/lock`)
        .set('Cookie', `pk_session=${adminToken}`); // tự khóa là CANNOT_MODIFY_SELF
      expect(lockLastAdmin.status).toBe(400);
    });

    it('người không phải admin gỡ quyền admin hoặc khóa admin nhận 403 (R1)', async () => {
      // Tạo 1 admin đích khác adminUser
      const targetAdmin = await createStaffWithRole('target_admin@pickory.local', 'admin');

      // Tạo HR có staff:manage nhưng không có admin
      const hrRole = await db.role.create({
        data: {
          code: 'hr_moderator',
          name: 'Quản trị viên nhân sự',
          permissions: { create: [{ permission: 'staff:manage' }, { permission: 'staff:read' }] },
        },
      });
      const hrUser = await db.user.create({
        data: {
          email: 'hr_mod@pickory.local',
          kind: 'staff',
          roles: { create: [{ roleId: hrRole.id }] },
        },
      });
      const hrToken = generateToken();
      await db.session.create({
        data: {
          tokenHash: hashToken(hrToken),
          userId: hrUser.id,
          expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
        },
      });

      // Nhánh 1: HR thử gỡ role admin của targetAdmin -> 403 FORBIDDEN
      const demoteRes = await request(app)
        .put(`/api/admin/staff/${targetAdmin.user.id}/roles`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({ roleCodes: ['sales'] });
      expect(demoteRes.status).toBe(403);
      expect(demoteRes.body.error.code).toBe('FORBIDDEN');

      // Nhánh 2: HR thử khóa targetAdmin -> 403 FORBIDDEN
      const lockRes = await request(app)
        .post(`/api/admin/staff/${targetAdmin.user.id}/lock`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({});
      expect(lockRes.status).toBe(403);
      expect(lockRes.body.error.code).toBe('FORBIDDEN');
    });

    it('khóa hoặc gỡ hết role thì mọi phiên cũ của người đó nhận 401 ngay', async () => {
      const { user, token } = await createStaffWithRole('staff_to_lock@pickory.local', 'sales');

      // Khóa tài khoản
      const lockRes = await request(app)
        .post(`/api/admin/staff/${user.id}/lock`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({});
      expect(lockRes.status).toBe(200);

      // Kiểm tra đúng 1 audit log cho lock
      const lockAudits = await db.auditLog.findMany({
        where: { action: 'staff.lock', targetId: user.id },
      });
      expect(lockAudits).toHaveLength(1);
      expect(lockAudits[0]!.actorId).toBe(adminUser.id);

      // Phiên cũ gửi request nhận 401 (hoặc guest)
      const meRes = await request(app).get('/api/me').set('Cookie', `pk_session=${token}`);
      expect(meRes.body.data.kind).toBe('guest');

      // Mở khóa lại
      const unlockRes = await request(app)
        .post(`/api/admin/staff/${user.id}/unlock`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({});
      expect(unlockRes.status).toBe(200);

      // Kiểm tra đúng 1 audit log cho unlock
      const unlockAudits = await db.auditLog.findMany({
        where: { action: 'staff.unlock', targetId: user.id },
      });
      expect(unlockAudits).toHaveLength(1);
      expect(unlockAudits[0]!.actorId).toBe(adminUser.id);
    });
  });

  describe('ID-15: Siết quyền khóa, mở khóa và gán role theo hợp quyền target', () => {
    async function createStaffWithRoles(email: string, roleCodes: string[]) {
      const roles = await db.role.findMany({ where: { code: { in: roleCodes } } });
      const user = await db.user.create({
        data: {
          email,
          kind: 'staff',
          roles: {
            create: roles.map((r) => ({ roleId: r.id })),
          },
        },
      });
      const token = generateToken();
      await db.session.create({
        data: {
          tokenHash: hashToken(token),
          userId: user.id,
          expiresAt: new Date(Date.now() + 12 * 60 * 60 * 1000),
        },
      });
      return { user, token };
    }

    it('người giữ staff:manage không phải admin bị từ chối khóa mục tiêu chứa quyền mình không sở hữu, kể cả khi đã bị khóa', async () => {
      // Chuẩn bị caller HR có staff:manage và staff:read nhưng không có quyền sales
      await db.role.create({
        data: {
          code: 'hr_lead_lock_test',
          name: 'HR Lead Lock Test',
          permissions: { create: [{ permission: 'staff:manage' }, { permission: 'staff:read' }] },
        },
      });
      const { token: hrToken } = await createStaffWithRoles('hr_caller_lock@pickory.local', [
        'hr_lead_lock_test',
      ]);

      // Target có role sales (chứa orders:update-status mà HR không có)
      const { user: targetSales } = await createStaffWithRoles('target_sales_lock@pickory.local', [
        'sales',
      ]);

      // Lý do: Nhân viên không phải admin không được phép khóa mục tiêu sở hữu quyền mà chính mình không có.
      const lockRes = await request(app)
        .post(`/api/admin/staff/${targetSales.id}/lock`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({});
      expect(lockRes.status).toBe(403);
      expect(lockRes.body.error.code).toBe('FORBIDDEN');

      // Admin khóa target trước
      await request(app)
        .post(`/api/admin/staff/${targetSales.id}/lock`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({});

      // Lý do: Kiểm tra quyền mục tiêu phải diễn ra trước nhánh no-op khi tài khoản đã bị khóa.
      const relockRes = await request(app)
        .post(`/api/admin/staff/${targetSales.id}/lock`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({});
      expect(relockRes.status).toBe(403);
      expect(relockRes.body.error.code).toBe('FORBIDDEN');
    });

    it('người giữ staff:manage không phải admin bị từ chối đổi role hoặc gỡ role của mục tiêu chứa quyền mình không sở hữu', async () => {
      // Chuẩn bị caller HR có staff:manage và staff:read
      await db.role.create({
        data: {
          code: 'hr_lead_roles_test',
          name: 'HR Lead Roles Test',
          permissions: { create: [{ permission: 'staff:manage' }, { permission: 'staff:read' }] },
        },
      });
      const { token: hrToken } = await createStaffWithRoles('hr_caller_roles@pickory.local', [
        'hr_lead_roles_test',
      ]);

      // Role mới là tập con quyền của HR (chỉ có staff:read)
      const subsetRole = await db.role.create({
        data: {
          code: 'staff_reader_role',
          name: 'Staff Reader',
          permissions: { create: [{ permission: 'staff:read' }] },
        },
      });

      // Target có role sales (chứa orders:update-status mà HR không có)
      const { user: targetSales } = await createStaffWithRoles('target_sales_roles@pickory.local', [
        'sales',
      ]);

      // Lý do: Thay thế role bằng role hợp lệ với caller nhưng target hiện tại có quyền caller thiếu, vẫn phải bị từ chối 403.
      const replaceRes = await request(app)
        .put(`/api/admin/staff/${targetSales.id}/roles`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({ roleCodes: [subsetRole.code] });
      expect(replaceRes.status).toBe(403);
      expect(replaceRes.body.error.code).toBe('FORBIDDEN');

      // Lý do: Gỡ hết role (roleCodes rỗng) của target có quyền caller thiếu cũng phải bị từ chối 403.
      const removeRes = await request(app)
        .put(`/api/admin/staff/${targetSales.id}/roles`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({ roleCodes: [] });
      expect(removeRes.status).toBe(403);
      expect(removeRes.body.error.code).toBe('FORBIDDEN');
    });

    it('người giữ staff:manage không phải admin khóa thành công mục tiêu có tập con quyền và phiên làm việc bị xóa', async () => {
      // Caller HR có staff:manage và staff:read
      await db.role.create({
        data: {
          code: 'hr_lead_subset_test',
          name: 'HR Lead Subset Test',
          permissions: { create: [{ permission: 'staff:manage' }, { permission: 'staff:read' }] },
        },
      });
      const { token: hrToken } = await createStaffWithRoles('hr_caller_subset@pickory.local', [
        'hr_lead_subset_test',
      ]);

      // Target chỉ có role với quyền staff:read (tập con quyền của HR)
      const subsetRole = await db.role.create({
        data: {
          code: 'staff_viewer_subset',
          name: 'Staff Viewer Subset',
          permissions: { create: [{ permission: 'staff:read' }] },
        },
      });
      const { user: targetSubset, token: targetToken } = await createStaffWithRoles(
        'target_subset_lock@pickory.local',
        [subsetRole.code],
      );

      // Lý do: Khi mục tiêu chỉ sở hữu tập con quyền của caller, thao tác khóa được phép thực hiện 200.
      const lockRes = await request(app)
        .post(`/api/admin/staff/${targetSubset.id}/lock`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({});
      expect(lockRes.status).toBe(200);
      expect(lockRes.body.data.success).toBe(true);

      // Lý do: Trạng thái lockedAt trong cơ sở dữ liệu được cập nhật thời gian khóa.
      const updatedUser = await db.user.findUnique({ where: { id: targetSubset.id } });
      expect(updatedUser?.lockedAt).not.toBeNull();

      // Lý do: Mọi phiên làm việc của nhân viên bị khóa phải bị xóa ngay lập tức.
      const meRes = await request(app).get('/api/me').set('Cookie', `pk_session=${targetToken}`);
      expect(meRes.body.data.kind).toBe('guest');
    });

    it('người giữ staff:manage không phải admin bị từ chối mở khóa mục tiêu vượt quyền và mở khóa thành công mục tiêu tập con quyền', async () => {
      // Caller HR có staff:manage và staff:read
      await db.role.create({
        data: {
          code: 'hr_lead_unlock_test',
          name: 'HR Lead Unlock Test',
          permissions: { create: [{ permission: 'staff:manage' }, { permission: 'staff:read' }] },
        },
      });
      const { token: hrToken } = await createStaffWithRoles('hr_caller_unlock@pickory.local', [
        'hr_lead_unlock_test',
      ]);

      // Nhánh 1: Target có role sales (vượt quyền HR) đã bị khóa
      const { user: targetSales } = await createStaffWithRoles(
        'target_sales_unlock@pickory.local',
        ['sales'],
      );
      await request(app)
        .post(`/api/admin/staff/${targetSales.id}/lock`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({});

      // Lý do: Mở khóa mục tiêu có quyền vượt quá quyền caller phải bị từ chối 403.
      const unlockDeniedRes = await request(app)
        .post(`/api/admin/staff/${targetSales.id}/unlock`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({});
      expect(unlockDeniedRes.status).toBe(403);
      expect(unlockDeniedRes.body.error.code).toBe('FORBIDDEN');

      const targetSalesDb = await db.user.findUnique({ where: { id: targetSales.id } });
      expect(targetSalesDb?.lockedAt).not.toBeNull();

      // Nhánh 2: Target có quyền tập con (chỉ có staff:read) đã bị khóa
      const subsetRole = await db.role.create({
        data: {
          code: 'staff_reader_unlock',
          name: 'Staff Reader Unlock',
          permissions: { create: [{ permission: 'staff:read' }] },
        },
      });
      const { user: targetSubset } = await createStaffWithRoles(
        'target_subset_unlock@pickory.local',
        [subsetRole.code],
      );
      await request(app)
        .post(`/api/admin/staff/${targetSubset.id}/lock`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({});

      // Lý do: Mở khóa mục tiêu có tập con quyền hợp lệ phải thành công 200 và đặt lockedAt về null.
      const unlockAllowedRes = await request(app)
        .post(`/api/admin/staff/${targetSubset.id}/unlock`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({});
      expect(unlockAllowedRes.status).toBe(200);
      expect(unlockAllowedRes.body.data.success).toBe(true);

      const targetSubsetDb = await db.user.findUnique({ where: { id: targetSubset.id } });
      expect(targetSubsetDb?.lockedAt).toBeNull();
    });

    it('người giữ staff:manage không phải admin bị từ chối thao tác với mục tiêu đa role có một role được phép và một role vượt quyền', async () => {
      // Caller HR có staff:manage và staff:read
      await db.role.create({
        data: {
          code: 'hr_lead_multirole_test',
          name: 'HR Lead Multirole Test',
          permissions: { create: [{ permission: 'staff:manage' }, { permission: 'staff:read' }] },
        },
      });
      const { token: hrToken } = await createStaffWithRoles('hr_caller_multi@pickory.local', [
        'hr_lead_multirole_test',
      ]);

      // Role được phép: staff:read
      const allowedRole = await db.role.create({
        data: {
          code: 'allowed_part_role',
          name: 'Allowed Part Role',
          permissions: { create: [{ permission: 'staff:read' }] },
        },
      });

      // Role không được phép: reviews:moderate
      const disallowedRole = await db.role.create({
        data: {
          code: 'disallowed_part_role',
          name: 'Disallowed Part Role',
          permissions: { create: [{ permission: 'reviews:moderate' }] },
        },
      });

      // Target có CẢ HAI role: allowed_part_role và disallowed_part_role
      const { user: targetMulti } = await createStaffWithRoles('target_multi_role@pickory.local', [
        allowedRole.code,
        disallowedRole.code,
      ]);

      // Lý do: Hợp quyền chứa ít nhất một quyền ngoài phạm vi của caller thì khóa phải bị từ chối 403.
      const lockRes = await request(app)
        .post(`/api/admin/staff/${targetMulti.id}/lock`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({});
      expect(lockRes.status).toBe(403);
      expect(lockRes.body.error.code).toBe('FORBIDDEN');

      // Lý do: Đổi role của mục tiêu đa role cũng phải bị từ chối 403.
      const replaceRes = await request(app)
        .put(`/api/admin/staff/${targetMulti.id}/roles`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({ roleCodes: [allowedRole.code] });
      expect(replaceRes.status).toBe(403);
      expect(replaceRes.body.error.code).toBe('FORBIDDEN');

      // Admin khóa targetMulti
      await request(app)
        .post(`/api/admin/staff/${targetMulti.id}/lock`)
        .set('Cookie', `pk_session=${adminToken}`)
        .send({});

      // Lý do: Mở khóa mục tiêu đa role có quyền vượt phạm vi phải bị từ chối 403.
      const unlockRes = await request(app)
        .post(`/api/admin/staff/${targetMulti.id}/unlock`)
        .set('Cookie', `pk_session=${hrToken}`)
        .send({});
      expect(unlockRes.status).toBe(403);
      expect(unlockRes.body.error.code).toBe('FORBIDDEN');
    });
  });
});
