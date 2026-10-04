import type { PrismaClient, Prisma } from '../../generated/prisma/client';
import { logger } from '../../shared/logger';

// Danh mục quyền khai báo trong code, không lưu trong DB: code là nơi kiểm tra quyền, admin chỉ gom quyền có sẵn thành role.
// Thêm quyền mới thuộc nhóm "Ask first" của dự án, và quyền mới phải có thêm mục trong PERMISSION_CATALOG bên dưới.
export const PERMISSIONS = [
  // identity
  'staff:read',
  'staff:manage',
  'roles:manage',
  'customers:read',
  'customers:manage',
  'audit-logs:read',
  // catalog
  'products:read',
  'products:write',
  'inventory:write',
  // commerce
  'orders:read',
  'orders:update-status',
  'orders:cancel',
  'orders:fulfill',
  'payments:read',
  'payments:reconcile',
  'reviews:moderate',
  'commerce-settings:manage',
  'refunds:manage',
  // support-desk
  'conversations:read',
  'conversations:reply',
  'support-settings:manage',
  // shipping
  'shipping-settings:manage',
  // knowledge-base
  'knowledge:read',
  'knowledge:write',
  // ai-gateway
  'ai-usage:read',
  'ai-settings:manage',
  // admin-console
  'reports:read',
  'reports:export',
] as const;

// Lấy kiểu từ mảng ở trên để requirePermission('...') báo lỗi ngay lúc biên dịch nếu gõ sai tên quyền.
export type Permission = (typeof PERMISSIONS)[number];

// Bảng tra cứu theo khóa, nhanh hơn duyệt mảng mỗi lần kiểm tra quyền.
export const PERMISSION_LOOKUP: Record<string, true> = Object.fromEntries(
  PERMISSIONS.map((p) => [p, true]),
);

// Kiểm tra một chuỗi (ví dụ từ DB hoặc từ client gửi lên) có thuộc danh mục quyền không.
export function isPermission(value: string): value is Permission {
  return PERMISSION_LOOKUP[value] === true;
}

export interface PermissionMetadata {
  id: Permission;
  // Nhóm dùng để giao diện hiển thị bảng tick quyền theo từng module.
  group: string;
  // Tên và mô tả tiếng Việt cho người dùng thấy trên giao diện, khác với id dùng trong code.
  name: string;
  description: string;
}

// Thông tin hiển thị của từng quyền, gửi cho giao diện qua GET /api/admin/permissions.
export const PERMISSION_CATALOG: PermissionMetadata[] = [
  // identity
  {
    id: 'staff:read',
    group: 'identity',
    name: 'Xem nhân viên',
    description: 'Xem danh sách tài khoản nhân viên',
  },
  {
    id: 'staff:manage',
    group: 'identity',
    name: 'Quản lý nhân viên',
    description: 'Mời, khóa, mở khóa nhân viên và gán role',
  },
  {
    id: 'roles:manage',
    group: 'identity',
    name: 'Quản lý role',
    description: 'Tạo, sửa, xóa role và phân quyền',
  },
  {
    id: 'customers:read',
    group: 'identity',
    name: 'Xem khách hàng',
    description: 'Xem danh sách và chi tiết tài khoản khách hàng',
  },
  {
    id: 'customers:manage',
    group: 'identity',
    name: 'Quản lý khách hàng',
    description: 'Khóa và mở khóa tài khoản khách hàng',
  },
  {
    id: 'audit-logs:read',
    group: 'identity',
    name: 'Xem nhật ký thao tác',
    description: 'Xem nhật ký hành động trong back-office',
  },
  // catalog
  {
    id: 'products:read',
    group: 'catalog',
    name: 'Xem sản phẩm',
    description: 'Xem sản phẩm bao gồm cả sản phẩm ẩn',
  },
  {
    id: 'products:write',
    group: 'catalog',
    name: 'Sửa sản phẩm',
    description: 'Tạo, sửa thông tin sản phẩm và danh mục',
  },
  {
    id: 'inventory:write',
    group: 'catalog',
    name: 'Sửa tồn kho',
    description: 'Cập nhật số lượng tồn kho sản phẩm',
  },
  // commerce
  {
    id: 'orders:read',
    group: 'commerce',
    name: 'Xem đơn hàng',
    description: 'Xem danh sách và chi tiết đơn hàng',
  },
  {
    id: 'orders:update-status',
    group: 'commerce',
    name: 'Đổi trạng thái đơn',
    description: 'Cập nhật trạng thái xử lý đơn hàng',
  },
  { id: 'orders:cancel', group: 'commerce', name: 'Hủy đơn hàng', description: 'Hủy đơn hàng' },
  {
    id: 'orders:fulfill',
    group: 'commerce',
    name: 'Vận chuyển đơn hàng',
    description: 'Tạo, hủy vận đơn GHN cho đơn hàng',
  },
  {
    id: 'payments:read',
    group: 'commerce',
    name: 'Xem thanh toán',
    description: 'Xem giao dịch và thanh toán',
  },
  {
    id: 'payments:reconcile',
    group: 'commerce',
    name: 'Đối soát thanh toán',
    description: 'Khớp tay giao dịch SePay',
  },
  {
    id: 'reviews:moderate',
    group: 'commerce',
    name: 'Kiểm duyệt đánh giá',
    description: 'Ẩn, hiện đánh giá sản phẩm của khách',
  },
  {
    id: 'commerce-settings:manage',
    group: 'commerce',
    name: 'Cấu hình thương mại',
    description: 'Ngưỡng freeship, hạn mức COD, tài khoản SePay',
  },
  {
    id: 'refunds:manage',
    group: 'commerce',
    name: 'Quản lý hoàn tiền',
    description: 'Đánh dấu đã hoàn tiền',
  },
  // support-desk
  {
    id: 'conversations:read',
    group: 'support-desk',
    name: 'Xem hội thoại CSKH',
    description: 'Xem hội thoại khách hàng',
  },
  {
    id: 'conversations:reply',
    group: 'support-desk',
    name: 'Trả lời CSKH',
    description: 'Tiếp nhận và phản hồi hội thoại chatbot chuyển sang',
  },
  {
    id: 'support-settings:manage',
    group: 'support-desk',
    name: 'Cấu hình CSKH',
    description: 'Giờ làm việc, ngày nghỉ, câu trả lời mẫu',
  },
  // shipping
  {
    id: 'shipping-settings:manage',
    group: 'shipping',
    name: 'Cấu hình vận chuyển',
    description: 'Địa chỉ lấy hàng, cấu hình kết nối GHN',
  },
  // knowledge-base
  {
    id: 'knowledge:read',
    group: 'knowledge-base',
    name: 'Xem cơ sở tri thức',
    description: 'Xem mọi tài liệu kể cả nội bộ',
  },
  {
    id: 'knowledge:write',
    group: 'knowledge-base',
    name: 'Soạn tài liệu tri thức',
    description: 'Soạn thảo, nhập và đăng tài liệu',
  },
  // ai-gateway
  {
    id: 'ai-usage:read',
    group: 'ai-gateway',
    name: 'Xem sử dụng AI',
    description: 'Xem thống kê chi phí và lượt gọi AI',
  },
  {
    id: 'ai-settings:manage',
    group: 'ai-gateway',
    name: 'Cấu hình AI',
    description: 'Sửa ngân sách và hạn mức AI',
  },
  // admin-console
  {
    id: 'reports:read',
    group: 'admin-console',
    name: 'Xem báo cáo',
    description: 'Xem dashboard và báo cáo tổng hợp',
  },
  {
    id: 'reports:export',
    group: 'admin-console',
    name: 'Xuất dữ liệu báo cáo',
    description: 'Xuất báo cáo, đơn hàng ra file Excel/CSV',
  },
];

// Quyền mặc định của các role tạo sẵn, theo bảng trong spec; chỉ dùng khi seed.
// Không có mục admin vì admin có mọi quyền ngầm định trong mergePermissions, kể cả quyền thêm sau này mà không cần seed lại.
export const DEFAULT_ROLE_PERMISSIONS: Record<string, Permission[]> = {
  customer: [],
  sales: [
    'customers:read',
    'products:read',
    'orders:read',
    'orders:update-status',
    'orders:cancel',
    'orders:fulfill',
    'payments:read',
    'payments:reconcile',
    'knowledge:read',
    'ai-usage:read',
    'reports:read',
  ],
  support: [
    'customers:read',
    'products:read',
    'orders:read',
    'reviews:moderate',
    'conversations:read',
    'conversations:reply',
    'knowledge:read',
  ],
};

// 4 role tạo sẵn; customer và admin là role hệ thống (isSystem) nên không sửa hay xóa được.
export const INITIAL_ROLES = [
  { code: 'customer', name: 'Khách hàng', isSystem: true },
  { code: 'admin', name: 'Quản trị viên', isSystem: true },
  { code: 'sales', name: 'Nhân viên bán hàng', isSystem: false },
  { code: 'support', name: 'Nhân viên CSKH', isSystem: false },
] as const;

// Tính quyền của một người từ các role họ giữ.
export function mergePermissions(
  roleCodes: string[],
  rawPermissions: string[],
  warnOnUnknown = true,
): Set<Permission> {
  // Admin có mọi quyền theo danh mục trong code, nên thêm quyền mới vào danh mục là admin có ngay.
  if (roleCodes.includes('admin')) {
    return new Set<Permission>(PERMISSIONS);
  }

  const result = new Set<Permission>();
  for (const raw of rawPermissions) {
    // Bỏ qua quyền không còn trong danh mục (ví dụ quyền đã bị xóa khỏi code), không để nó lọt vào quyền hiệu lực.
    if (isPermission(raw)) {
      result.add(raw);
    } else if (warnOnUnknown) {
      // Cảnh báo để người vận hành biết còn dữ liệu cũ trong DB; middleware tắt cảnh báo này vì đã có kiểm tra lúc khởi động.
      logger.warn(
        { permission: raw, roles: roleCodes },
        'Quyền lạ trong role_permissions bị bỏ qua',
      );
    }
  }

  return result;
}

// Chạy một lần lúc khởi động server để cảnh báo quyền lạ trong DB.
export async function validateStoredPermissions(
  dbClient: PrismaClient | Prisma.TransactionClient,
): Promise<string[]> {
  const allPermissions = await dbClient.rolePermission.findMany({
    select: {
      permission: true,
      role: { select: { code: true } },
    },
  });
  const unknownPermissions: string[] = [];
  for (const item of allPermissions) {
    // Quyền còn trong DB nhưng đã bị bỏ khỏi code: chỉ cảnh báo, không xóa, vì xóa dữ liệu là quyết định của người vận hành.
    if (!isPermission(item.permission)) {
      unknownPermissions.push(item.permission);
      logger.warn(
        { permission: item.permission, role: item.role.code },
        'Quyền lạ trong role_permissions được phát hiện khi khởi động',
      );
    }
  }
  return unknownPermissions;
}
