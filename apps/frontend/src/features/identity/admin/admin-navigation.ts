import type { IconName } from '../../../shared/icons';
import { checkPermission, type ActorData } from '../hooks/use-actor';

export interface AdminNavItem {
  label: string;
  path: string;
  icon: IconName;
  // Không có quyền nghĩa là mọi nhân viên đều thấy mục này.
  requiredPermission?: string;
  // Đường dẫn con thuộc mục khác dù bắt đầu bằng `path`, ví dụ trang tồn kho SKU nằm dưới `/admin/products`.
  inactiveOn?: RegExp;
  // Đường dẫn ngoài `path` nhưng vẫn thuộc mục này.
  activeOn?: RegExp;
}

export interface AdminNavGroup {
  // Nhóm đầu không có nhãn, giống bản mẫu `shop-retail/`.
  label: string | null;
  items: AdminNavItem[];
}

// Trang tồn kho của một SKU. Trang này thuộc mục Tồn kho dù đường dẫn nằm dưới `/admin/products`.
const SKU_INVENTORY_PATH = /^\/admin\/products\/[^/]+\/skus\/[^/]+\/inventory\/?$/;

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
        label: 'Đối soát',
        path: '/admin/payments',
        icon: 'receipt',
        requiredPermission: 'payments:read',
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
      {
        label: 'Đánh giá',
        path: '/admin/reviews',
        icon: 'star',
        requiredPermission: 'reviews:moderate',
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
        inactiveOn: SKU_INVENTORY_PATH,
      },
      {
        label: 'Tồn kho',
        path: '/admin/inventory',
        icon: 'inventory',
        requiredPermission: 'products:read',
        activeOn: SKU_INVENTORY_PATH,
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
    label: 'Báo cáo',
    items: [
      {
        label: 'Doanh thu thương hiệu',
        path: '/admin/reports/revenue/brands',
        icon: 'chart',
        requiredPermission: 'reports:read',
      },
      {
        label: 'Doanh thu theo kỳ',
        path: '/admin/reports/revenue/periods',
        icon: 'chart',
        requiredPermission: 'reports:read',
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
        label: 'Vận chuyển',
        path: '/admin/shipping/settings',
        icon: 'truck',
        requiredPermission: 'shipping-settings:manage',
      },
      {
        label: 'Phí ship dự phòng',
        path: '/admin/shipping/fallback-fee',
        icon: 'receipt',
        requiredPermission: 'shipping-settings:manage',
      },
      {
        label: 'Cấu hình bán hàng',
        path: '/admin/commerce/settings',
        icon: 'receipt',
        requiredPermission: 'commerce-settings:manage',
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

// Mục menu có đang mở không. `/admin` chỉ khớp đúng nó, các mục khác khớp cả trang con.
export function isNavItemActive(item: AdminNavItem, pathname: string): boolean {
  if (item.activeOn?.test(pathname)) return true;
  if (item.inactiveOn?.test(pathname)) return false;
  if (item.path === '/admin') return pathname === '/admin';
  return pathname === item.path || pathname.startsWith(`${item.path}/`);
}
