import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { recordAudit } from './audit';

export interface GetCustomersParams {
  search?: string;
  page?: number;
  limit?: number;
}

/**
 * Tra cứu danh sách khách hàng có phân trang và tìm kiếm.
 */
export async function getCustomers(params: GetCustomersParams = {}) {
  const { search, page = 1, limit = 20 } = params;

  // Chỉ truy vấn tài khoản thuộc loại customer, loại trừ nhân viên quản trị khỏi danh sách khách hàng.
  const where: Record<string, unknown> = {
    kind: 'customer',
  };

  // Cho phép tìm kiếm linh hoạt theo cả tên, email và số điện thoại để hỗ trợ nhân viên chăm sóc khách hàng.
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
      // Tuyệt đối không chọn trường passwordHash để loại bỏ rủi ro rò rỉ mã băm mật khẩu ra API.
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
 * Lấy thông tin chi tiết một khách hàng theo định danh người dùng.
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

  // Kiểm tra kind để đảm bảo không xem nhầm thông tin nhân viên qua endpoint quản lý khách hàng.
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
 * Khóa tài khoản khách hàng và chấm dứt ngay lập tức các phiên hoạt động.
 */
export async function lockCustomer(params: CustomerLockParams): Promise<void> {
  const { actorId, customerId } = params;

  const customer = await db.user.findUnique({
    where: { id: customerId },
  });

  // Chặn thao tác nếu tài khoản là nhân viên để tránh dùng nhầm chức năng khóa khách hàng lên tài khoản nội bộ.
  if (!customer || customer.kind !== 'customer') {
    throw new AppError('CUSTOMER_NOT_FOUND', 404, 'Không tìm thấy khách hàng.');
  }

  await db.$transaction(async (tx) => {
    await tx.user.update({
      where: { id: customerId },
      data: { lockedAt: new Date() },
    });

    // Thu hồi ngay lập tức mọi phiên đăng nhập để tài khoản bị khóa không thể tiếp tục thực hiện giao dịch.
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
 * Mở khóa tài khoản khách hàng để khôi phục quyền truy cập.
 */
export async function unlockCustomer(params: CustomerLockParams): Promise<void> {
  const { actorId, customerId } = params;

  const customer = await db.user.findUnique({
    where: { id: customerId },
  });

  // Chỉ xử lý mở khóa cho tài khoản khách hàng, ngăn chặn thao tác nhầm trên tài khoản nhân viên.
  if (!customer || customer.kind !== 'customer') {
    throw new AppError('CUSTOMER_NOT_FOUND', 404, 'Không tìm thấy khách hàng.');
  }

  await db.$transaction(async (tx) => {
    // Đặt lại lockedAt về null để cho phép người dùng đăng nhập bình thường trở lại.
    await tx.user.update({
      where: { id: customerId },
      data: { lockedAt: null },
    });

    // Ghi lại hành động mở khóa vào nhật ký kiểm toán để theo dõi trách nhiệm của nhân viên quản trị.
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
