import { Router } from 'express';
import { z } from 'zod';
import { db } from '../../../shared/db';
import { requirePermission } from '../security/middleware';

// Nhật ký thao tác back-office, chỉ đọc.
export const adminAuditRouter = Router();

const auditQuerySchema = z.object({
  actorId: z.string().optional(),
  action: z.string().optional(),
  targetType: z.string().optional(),
  // datetime() đòi chuỗi ISO có múi giờ (đuôi Z), nên giao diện phải đổi ngày sang ISO trước khi gửi.
  startDate: z.string().datetime().optional(),
  endDate: z.string().datetime().optional(),
  // coerce vì query string luôn là chuỗi.
  page: z.coerce.number().int().min(1).default(1),
  // Trần 100 dòng mỗi trang để một request không kéo về cả bảng nhật ký.
  limit: z.coerce.number().int().min(1).max(100).default(20),
});

// GET /api/admin/audit-logs: xem nhật ký, chỉ đọc (cố ý không có route sửa hay xóa).
adminAuditRouter.get(
  '/api/admin/audit-logs',
  requirePermission('audit-logs:read'),
  async (req, res, next) => {
    try {
      const query = auditQuerySchema.parse(req.query);
      const where: Record<string, unknown> = {};

      // Chỉ thêm điều kiện lọc cho tham số nào có gửi lên.
      if (query.actorId) where.actorId = query.actorId;
      if (query.action) where.action = query.action;
      if (query.targetType) where.targetType = query.targetType;

      if (query.startDate || query.endDate) {
        const createdAt: Record<string, Date> = {};
        // gte và lte để khoảng thời gian gồm cả hai đầu mút.
        if (query.startDate) createdAt.gte = new Date(query.startDate);
        if (query.endDate) createdAt.lte = new Date(query.endDate);
        where.createdAt = createdAt;
      }

      const skip = (query.page - 1) * query.limit;
      const [items, total] = await Promise.all([
        db.auditLog.findMany({
          where,
          // Mới nhất lên đầu vì người xem thường quan tâm thao tác gần đây.
          orderBy: { createdAt: 'desc' },
          skip,
          take: query.limit,
        }),
        db.auditLog.count({ where }),
      ]);

      // Nhật ký chỉ lưu actorId nên tra thêm email và tên để giao diện hiển thị người thực hiện thay vì chuỗi UUID.
      // Lấy một lần cho cả trang (theo danh sách id không trùng) thay vì mỗi dòng một truy vấn.
      const actorIds = [...new Set(items.map((i) => i.actorId))];
      const actors = await db.user.findMany({
        where: { id: { in: actorIds } },
        select: { id: true, email: true, name: true },
      });
      const actorMap = new Map(actors.map((a) => [a.id, a]));

      const enrichedItems = items.map((item) => {
        const actor = actorMap.get(item.actorId);
        return {
          ...item,
          // null khi không tìm thấy người đó để giao diện hiển thị phương án dự phòng.
          actorEmail: actor?.email ?? null,
          actorName: actor?.name ?? null,
        };
      });

      res.json({
        data: {
          items: enrichedItems,
          total,
          page: query.page,
          limit: query.limit,
        },
      });
    } catch (err) {
      next(err);
    }
  },
);
