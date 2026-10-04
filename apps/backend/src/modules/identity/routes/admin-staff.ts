import { Router } from 'express';
import { z } from 'zod';
import { requirePermission } from '../security/middleware';
import {
  assignStaffRoles,
  getStaffList,
  inviteStaff,
  lockStaff,
  unlockStaff,
} from '../services/staff';

// Quản lý nhân viên: danh sách, mời, gán role, khóa và mở khóa.
export const adminStaffRouter = Router();

// GET /api/admin/staff: danh sách nhân viên.
adminStaffRouter.get(
  '/api/admin/staff',
  requirePermission('staff:read'),
  async (_req, res, next) => {
    try {
      const staff = await getStaffList();
      res.json({ data: staff });
    } catch (err) {
      next(err);
    }
  },
);

const inviteStaffSchema = z.object({
  email: z.string().email('Email không đúng định dạng.'),
  name: z.string().trim().min(1, 'Họ và tên không được để trống.'),
  // Bắt buộc ít nhất một role, vì nhân viên không có role nào thì không làm được gì.
  roleCodes: z
    .array(z.string())
    .min(1, 'Cần chọn ít nhất một vai trò.')
    .transform((items) => [...new Set(items)]),
});

// POST /api/admin/staff/invitations: mời nhân viên mới.
adminStaffRouter.post(
  '/api/admin/staff/invitations',
  requirePermission('staff:manage'),
  async (req, res, next) => {
    try {
      const { email, name, roleCodes } = inviteStaffSchema.parse(req.body);
      // Truyền role và quyền của người mời để chặn họ mời người vào role mạnh hơn quyền của mình.
      const result = await inviteStaff({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        email,
        name,
        roleCodes,
      });
      res.status(201).json({ data: result });
    } catch (err) {
      next(err);
    }
  },
);

const assignRolesSchema = z.object({
  // Cho phép mảng rỗng, nghĩa là gỡ hết role (khi đó phiên của người đó bị xóa).
  roleCodes: z.array(z.string()).transform((items) => [...new Set(items)]),
});

// PUT /api/admin/staff/:id/roles: đặt lại toàn bộ role của một nhân viên.
adminStaffRouter.put(
  '/api/admin/staff/:id/roles',
  requirePermission('staff:manage'),
  async (req, res, next) => {
    try {
      const targetUserId = req.params.id as string;
      const { roleCodes } = assignRolesSchema.parse(req.body);
      const result = await assignStaffRoles({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        targetUserId,
        roleCodes,
      });
      res.json({ data: result });
    } catch (err) {
      next(err);
    }
  },
);

// POST /api/admin/staff/:id/lock: khóa nhân viên.
adminStaffRouter.post(
  '/api/admin/staff/:id/lock',
  requirePermission('staff:manage'),
  async (req, res, next) => {
    try {
      const targetUserId = req.params.id as string;
      await lockStaff({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        targetUserId,
      });
      res.json({ data: { success: true, message: 'Đã khóa tài khoản nhân viên.' } });
    } catch (err) {
      next(err);
    }
  },
);

// POST /api/admin/staff/:id/unlock: mở khóa nhân viên.
adminStaffRouter.post(
  '/api/admin/staff/:id/unlock',
  requirePermission('staff:manage'),
  async (req, res, next) => {
    try {
      const targetUserId = req.params.id as string;
      await unlockStaff({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        targetUserId,
      });
      res.json({ data: { success: true, message: 'Đã mở khóa tài khoản nhân viên.' } });
    } catch (err) {
      next(err);
    }
  },
);
