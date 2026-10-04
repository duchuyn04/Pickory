import { Router } from 'express';
import { z } from 'zod';
import { AppError } from '../../../shared/errors';
import { requirePermission, requireStaff } from '../security/middleware';
import {
  createRole,
  deleteRole,
  getPermissionsCatalog,
  getRoles,
  updateRole,
} from '../services/roles';

// Quản lý role và danh mục quyền.
export const adminRolesRouter = Router();

// GET /api/admin/permissions: danh mục quyền cho bảng tick trên giao diện quản lý role.
adminRolesRouter.get('/api/admin/permissions', requirePermission('roles:manage'), (_req, res) => {
  res.json({ data: getPermissionsCatalog() });
});

// GET /api/admin/roles: danh sách role; cho cả người quản lý nhân viên xem vì họ cần chọn role khi mời hay gán.
adminRolesRouter.get('/api/admin/roles', requireStaff, async (req, res, next) => {
  try {
    const hasRolesManage = req.actor.permissions.has('roles:manage');
    const hasStaffManage = req.actor.permissions.has('staff:manage');
    // Phải có một trong hai quyền; không dùng requirePermission vì nó đòi đủ tất cả quyền liệt kê, ở đây chỉ cần một.
    if (!hasRolesManage && !hasStaffManage) {
      throw new AppError('FORBIDDEN', 403, 'Bạn không có quyền xem danh sách vai trò.');
    }
    const roles = await getRoles();
    res.json({ data: roles });
  } catch (err) {
    next(err);
  }
});

const createRoleSchema = z.object({
  // trim trước min để tên toàn khoảng trắng bị coi là trống.
  code: z.string().trim().min(1, 'Mã role không được để trống.'),
  name: z.string().trim().min(1, 'Tên role không được để trống.'),
  permissions: z
    .array(z.string())
    .default([])
    // Khử trùng ngay ở đây để quyền gửi hai lần không gây lỗi khóa chính trùng ở DB.
    .transform((items) => [...new Set(items)]),
});

// POST /api/admin/roles: tạo role mới.
adminRolesRouter.post(
  '/api/admin/roles',
  requirePermission('roles:manage'),
  async (req, res, next) => {
    try {
      const { code, name, permissions } = createRoleSchema.parse(req.body);
      // Truyền role và quyền của người gọi xuống để chặn họ cấp quyền vượt quá quyền của chính họ.
      const role = await createRole({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        code,
        name,
        permissions,
      });
      res.status(201).json({ data: role });
    } catch (err) {
      next(err);
    }
  },
);

const updateRoleSchema = z.object({
  name: z.string().trim().min(1, 'Tên role không được để trống.').optional(),
  permissions: z
    .array(z.string())
    .optional()
    // Không gửi permissions thì giữ undefined để chỉ đổi tên, không xóa hết quyền.
    .transform((items) => (items ? [...new Set(items)] : undefined)),
});

// PATCH /api/admin/roles/:id: sửa tên hoặc quyền của role.
adminRolesRouter.patch(
  '/api/admin/roles/:id',
  requirePermission('roles:manage'),
  async (req, res, next) => {
    try {
      const roleId = req.params.id as string;
      const { name, permissions } = updateRoleSchema.parse(req.body);
      const role = await updateRole({
        actorId: req.actor.userId!,
        actorRoles: req.actor.roles,
        actorPermissions: req.actor.permissions,
        roleId,
        name,
        permissions,
      });
      res.json({ data: role });
    } catch (err) {
      next(err);
    }
  },
);

// DELETE /api/admin/roles/:id: xóa role chưa ai giữ.
adminRolesRouter.delete(
  '/api/admin/roles/:id',
  requirePermission('roles:manage'),
  async (req, res, next) => {
    try {
      const roleId = req.params.id as string;
      await deleteRole(req.actor.userId!, roleId);
      res.json({ data: { success: true } });
    } catch (err) {
      next(err);
    }
  },
);
