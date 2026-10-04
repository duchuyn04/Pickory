import { Router } from 'express';
import { z } from 'zod';
import { requirePermission } from '../security/middleware';
import { getCustomerById, getCustomers, lockCustomer, unlockCustomer } from '../services/customers';

// Tra cứu, khóa và mở khóa tài khoản khách.
export const adminCustomersRouter = Router();

const customerQuerySchema = z.object({
  search: z.string().optional(),
  page: z.coerce.number().int().min(1).default(1),
  limit: z.coerce.number().int().min(1).max(100).default(20),
});

// GET /api/admin/customers: tra cứu khách, cho cả sales và support xem (customers:read).
adminCustomersRouter.get(
  '/api/admin/customers',
  requirePermission('customers:read'),
  async (req, res, next) => {
    try {
      const query = customerQuerySchema.parse(req.query);
      const result = await getCustomers(query);
      res.json({ data: result });
    } catch (err) {
      next(err);
    }
  },
);

// GET /api/admin/customers/:id: xem một khách.
adminCustomersRouter.get(
  '/api/admin/customers/:id',
  requirePermission('customers:read'),
  async (req, res, next) => {
    try {
      const customerId = req.params.id as string;
      const customer = await getCustomerById(customerId);
      res.json({ data: customer });
    } catch (err) {
      next(err);
    }
  },
);

// POST /api/admin/customers/:id/lock: khóa khách, cần quyền customers:manage (sales và support chỉ có quyền xem).
adminCustomersRouter.post(
  '/api/admin/customers/:id/lock',
  requirePermission('customers:manage'),
  async (req, res, next) => {
    try {
      const customerId = req.params.id as string;
      await lockCustomer({
        actorId: req.actor.userId!,
        customerId,
      });
      res.json({ data: { success: true, message: 'Đã khóa tài khoản khách hàng.' } });
    } catch (err) {
      next(err);
    }
  },
);

// POST /api/admin/customers/:id/unlock: mở khóa khách.
adminCustomersRouter.post(
  '/api/admin/customers/:id/unlock',
  requirePermission('customers:manage'),
  async (req, res, next) => {
    try {
      const customerId = req.params.id as string;
      await unlockCustomer({
        actorId: req.actor.userId!,
        customerId,
      });
      res.json({ data: { success: true, message: 'Đã mở khóa tài khoản khách hàng.' } });
    } catch (err) {
      next(err);
    }
  },
);
