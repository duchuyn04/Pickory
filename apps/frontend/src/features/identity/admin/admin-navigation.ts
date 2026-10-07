import type { IconName } from '../../../shared/icons';
import { checkPermission, type ActorData } from '../hooks/use-actor';

export interface AdminNavItem {
  label: string;
  path: string;
  icon: IconName;
  // Không có quyền nghĩa là mọi nhân viên đều thấy mục này.
  requiredPermission?: string;
}

export interface AdminNavGroup {
  // Nhóm đầu không có nhãn, giống bản mẫu `shop-retail/`.
  label: string | null;
  items: AdminNavItem[];
}

// Chỉ liệt kê mục có route trong `app.tsx`. Mục của module chưa làm có trang giữ chỗ, nên bấm không ra trang trắng.
export const ADMIN_NAV_GROUPS: AdminNavGroup[] = [
  {
    label: null,
    items: [{ label: 'Tổng quan', path: '/admin', icon: 'dashboard' }],
  },
  {
    label: 'Bán hàng',
    items: [
      {
        label: 'Đơn hàng',
        path: '/admin/orders',
        icon: 'receipt',
        requiredPermission: 'orders:read',
      },
      {
        label: 'Khách hàng',
        path: '/admin/customers',
        icon: 'users',
        requiredPermission: 'customers:read',
      },
      {
        label: 'CSKH',
        path: '/admin/conversations',
        icon: 'message',
        requiredPermission: 'conversations:read',
      },
    ],
  },
  {
    label: 'Sản phẩm',
    items: [
      {
        label: 'Sản phẩm',
        path: '/admin/products',
        icon: 'package',
        requiredPermission: 'products:read',
      },
      {
        label: 'Danh mục',
        path: '/admin/categories',
        icon: 'folder',
        requiredPermission: 'products:read',
      },
      {
        label: 'Thương hiệu',
        path: '/admin/brands',
        icon: 'tag',
        requiredPermission: 'products:read',
      },
    ],
  },
  {
    label: 'Hệ thống',
    items: [
      { label: 'Nhân viên', path: '/admin/staff', icon: 'staff', requiredPermission: 'staff:read' },
      {
        label: 'Quản lý Role',
        path: '/admin/roles',
        icon: 'shield',
        requiredPermission: 'roles:manage',
      },
      {
        label: 'Cấu hình AI',
        path: '/admin/ai',
        icon: 'sparkles',
        requiredPermission: 'ai-usage:read',
      },
      {
        label: 'Nhật ký thao tác',
        path: '/admin/audit-logs',
        icon: 'history',
        requiredPermission: 'audit-logs:read',
      },
    ],
  },
];

// Ẩn mục thiếu quyền để sales không thấy menu quản trị hệ thống. Backend vẫn tự kiểm quyền mọi request.
export function filterNavGroupsByPermission(
  groups: AdminNavGroup[],
  actor: ActorData,
): AdminNavGroup[] {
  return groups
    .map((group) => ({
      ...group,
      items: group.items.filter(
        (item) => !item.requiredPermission || checkPermission(actor, item.requiredPermission),
      ),
    }))
    .filter((group) => group.items.length > 0);
}
