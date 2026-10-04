import { db } from '../../shared/db';
import { AppError } from '../../shared/errors';
import { recordAudit } from './audit';

export interface GetCustomersParams {
  search?: string;
  page?: number;
  limit?: number;
}

// Tra cứu khách hàng cho nhân viên, có tìm kiếm và phân trang.
export async function getCustomers(params: GetCustomersParams = {}) {
  const { search, page = 1, limit = 20 } = params;

  // Chỉ lấy khách; nhân viên có trang quản lý riêng và không được lẫn vào danh sách này.
  const where: Record<string, unknown> = {
    kind: 'customer',
  };

  if (search && search.trim() !== '') {
    const q = search.trim();
    // Tìm theo email, tên hoặc số điện thoại vì nhân viên hỗ trợ có thể chỉ có một trong ba thông tin khi khách gọi tới.
    where.OR = [
      { email: { contains: q, mode: 'insensitive' } },
      { name: { contains: q, mode: 'insensitive' } },
      // Số điện thoại không phân biệt hoa thường nên không cần mode.
      { phone: { contains: q } },
    ];
  }

  // Bỏ qua các dòng của những trang trước.
  const skip = (page - 1) * limit;
  // Lấy dữ liệu và tổng số cùng lúc, vì hai truy vấn không phụ thuộc nhau.
  const [items, total] = await Promise.all([
    db.user.findMany({
      where,
      // Liệt kê rõ từng trường thay vì lấy hết, để passwordHash không bao giờ lọt vào kết quả trả ra API.
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
      // Khách mới nhất lên đầu, vì nhân viên thường cần tìm người vừa đăng ký.
      orderBy: { createdAt: 'desc' },
      skip,
      take: limit,
    }),
    // Đếm cùng điều kiện lọc để giao diện biết có bao nhiêu trang.
    db.user.count({ where }),
  ]);

  return {
    items,
    total,
    page,
    limit,
  };
}

// Lấy một khách theo id.
export async function getCustomerById(customerId: string) {
  const customer = await db.user.findUnique({
    where: { id: customerId },
    // Cùng danh sách trường như getCustomers, không có passwordHash.
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

  // Id của nhân viên cũng báo không tìm thấy, để endpoint này không bị dùng xem hồ sơ nhân viên.
  if (!customer || customer.kind !== 'customer') {
    throw new AppError('CUSTOMER_NOT_FOUND', 404, 'Không tìm thấy khách hàng.');
  }

  return customer;
}

export interface CustomerLockParams {
  actorId: string;
  customerId: string;
}

// Khóa khách: không đăng nhập được nữa và phiên đang mở mất hiệu lực ngay.
export async function lockCustomer(params: CustomerLockParams): Promise<void> {
  const { actorId, customerId } = params;

  const customer = await db.user.findUnique({
    where: { id: customerId },
  });

  // Chặn khóa nhầm tài khoản nhân viên qua endpoint khách; nhân viên phải khóa qua staff:manage.
  if (!customer || customer.kind !== 'customer') {
    throw new AppError('CUSTOMER_NOT_FOUND', 404, 'Không tìm thấy khách hàng.');
  }

  // Dùng transaction để ba việc cùng thành công hoặc cùng bị hủy: khóa, xóa phiên, ghi nhật ký.
  await db.$transaction(async (tx) => {
    await tx.user.update({
      where: { id: customerId },
      data: { lockedAt: new Date() },
    });

    // Xóa phiên để khách bị đăng xuất ngay, không phải chờ phiên hết hạn.
    await tx.session.deleteMany({
      where: { userId: customerId },
    });

    // Ghi nhật ký trong cùng transaction để không có thao tác khóa nào thiếu dấu vết.
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

// Mở khóa khách để họ đăng nhập lại được.
export async function unlockCustomer(params: CustomerLockParams): Promise<void> {
  const { actorId, customerId } = params;

  const customer = await db.user.findUnique({
    where: { id: customerId },
  });

  // Cũng chỉ áp dụng cho khách, vì lý do giống lockCustomer.
  if (!customer || customer.kind !== 'customer') {
    throw new AppError('CUSTOMER_NOT_FOUND', 404, 'Không tìm thấy khách hàng.');
  }

  await db.$transaction(async (tx) => {
    // lockedAt về null nghĩa là không bị khóa.
    await tx.user.update({
      where: { id: customerId },
      data: { lockedAt: null },
    });

    // Không cần xóa phiên vì khi bị khóa phiên đã bị xóa hết; khách phải đăng nhập lại.
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
