import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { recordAudit } from './audit';

export interface GetCustomersParams {
  search?: string;
  page?: number;
  limit?: number;
}

/**
 * Tra cứu danh sách khách hàng (không bao giờ trả về passwordHash).
 */
export async function getCustomers(params: GetCustomersParams = {}) {
  const { search, page = 1, limit = 20 } = params;

  const where: Record<string, unknown> = {
    kind: 'customer',
  };

  if (search && search.trim() !== '') {
    const q = search.trim();
    where.OR = [
      { email: { contains: q, mode: 'insensitive' } },
      { name: { contains: q, mode: 'insensitive' } },
      { phone: { contains: q } },
    ];
  }

  const skip = (page - 1) * limit;
  const [items, total] = await Promise.all([
    db.user.findMany({
      where,
      select: {
        id: true,
        email: true,
        name: true,
        phone: true,
        emailVerified: true,
        kind: true,
        lockedAt: true,
        createdAt: true,
      },
      orderBy: { createdAt: 'desc' },
      skip,
      take: limit,
    }),
    db.user.count({ where }),
  ]);

  return {
    items,
    total,
    page,
    limit,
  };
}

/**
 * Lấy chi tiết thông tin một khách hàng theo id (không bao giờ trả về passwordHash).
 */
export async function getCustomerById(customerId: string) {
  const customer = await db.user.findUnique({
    where: { id: customerId },
    select: {
      id: true,
      email: true,
      name: true,
      phone: true,
      emailVerified: true,
      kind: true,
      lockedAt: true,
      createdAt: true,
    },
  });

  if (!customer || customer.kind !== 'customer') {
    throw new AppError('CUSTOMER_NOT_FOUND', 404, 'Không tìm thấy khách hàng.');
  }

  return customer;
}

export interface CustomerLockParams {
  actorId: string;
  customerId: string;
}

/**
 * Khóa tài khoản khách hàng:
 * 1. Đặt lockedAt.
 * 2. Xóa mọi phiên đang hoạt động của khách hàng.
 * 3. Ghi 1 dòng audit log.
 */
export async function lockCustomer(params: CustomerLockParams): Promise<void> {
  const { actorId, customerId } = params;

  const customer = await db.user.findUnique({
    where: { id: customerId },
  });

  if (!customer || customer.kind !== 'customer') {
    throw new AppError('CUSTOMER_NOT_FOUND', 404, 'Không tìm thấy khách hàng.');
  }

  await db.$transaction(async (tx) => {
    await tx.user.update({
      where: { id: customerId },
      data: { lockedAt: new Date() },
    });

    // Xóa mọi phiên cũ ngay
    await tx.session.deleteMany({
      where: { userId: customerId },
    });

    await recordAudit(tx, {
      actorId,
      action: 'customer.lock',
      targetType: 'user',
      targetId: customerId,
      before: { lockedAt: customer.lockedAt },
      after: { lockedAt: new Date() },
    });
  });
}

/**
 * Mở khóa tài khoản khách hàng:
 * 1. Đặt lockedAt = null.
 * 2. Ghi 1 dòng audit log.
 */
export async function unlockCustomer(params: CustomerLockParams): Promise<void> {
  const { actorId, customerId } = params;

  const customer = await db.user.findUnique({
    where: { id: customerId },
  });

  if (!customer || customer.kind !== 'customer') {
    throw new AppError('CUSTOMER_NOT_FOUND', 404, 'Không tìm thấy khách hàng.');
  }

  await db.$transaction(async (tx) => {
    await tx.user.update({
      where: { id: customerId },
      data: { lockedAt: null },
    });

    await recordAudit(tx, {
      actorId,
      action: 'customer.unlock',
      targetType: 'user',
      targetId: customerId,
      before: { lockedAt: customer.lockedAt },
      after: { lockedAt: null },
    });
  });
}
