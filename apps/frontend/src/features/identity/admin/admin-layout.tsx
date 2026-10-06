import { useQueryClient } from '@tanstack/react-query';
import { Link, Navigate, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { checkPermission, useActor } from '../hooks/use-actor';

interface NavItem {
  label: string;
  path: string;
  // Không có nghĩa là mục nào nhân viên cũng thấy.
  requiredPermission?: string;
}

// Mục menu cùng quyền cần có để thấy mục đó; mục của module chưa làm vẫn có route giữ chỗ nên bấm không ra trang trắng.
const NAV_ITEMS: NavItem[] = [
  { label: 'Tổng quan', path: '/admin' },
  { label: 'Đơn hàng', path: '/admin/orders', requiredPermission: 'orders:read' },
  { label: 'Sản phẩm', path: '/admin/products', requiredPermission: 'products:read' },
  { label: 'Danh mục', path: '/admin/categories', requiredPermission: 'products:read' },
  { label: 'Thương hiệu', path: '/admin/brands', requiredPermission: 'products:read' },
  { label: 'Khách hàng', path: '/admin/customers', requiredPermission: 'customers:read' },
  { label: 'CSKH', path: '/admin/conversations', requiredPermission: 'conversations:read' },
  { label: 'Nhân viên', path: '/admin/staff', requiredPermission: 'staff:read' },
  { label: 'Quản lý Role', path: '/admin/roles', requiredPermission: 'roles:manage' },
  { label: 'Cấu hình AI', path: '/admin/ai', requiredPermission: 'ai-usage:read' },
  { label: 'Nhật ký thao tác', path: '/admin/audit-logs', requiredPermission: 'audit-logs:read' },
];

// Khung chung của khu vực quản trị: kiểm tra đăng nhập, vẽ menu theo quyền, chứa trang con.
export function AdminLayout() {
  const { data: actor, isLoading } = useActor();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();

  // Chưa biết ai đang đăng nhập thì chưa chuyển hướng, nếu không nhân viên đã đăng nhập bị đẩy ra trang login oan.
  if (isLoading) {
    return (
      <div
        style={{ padding: 'var(--space-6)', fontFamily: 'var(--font-sans)', textAlign: 'center' }}
      >
        Đang nạp dữ liệu...
      </div>
    );
  }

  // Khách vãng lai, khách hàng, và nhân viên vừa mất phiên đều về trang đăng nhập nhân viên.
  // Đây chỉ là tiện dụng cho người dùng; dữ liệu thật vẫn do backend chặn bằng requirePermission.
  if (!actor || actor.kind !== 'staff') {
    return <Navigate to="/admin/login" replace />;
  }

  async function handleLogout() {
    try {
      await apiRequest('/api/auth/logout', { method: 'POST' });
    } finally {
      // Dùng finally để dù gọi đăng xuất lỗi (mạng) vẫn nạp lại actor và về trang đăng nhập, không kẹt ở khu vực quản trị.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      navigate('/admin/login', { replace: true });
    }
  }

  // Chỉ hiện mục mà nhân viên có quyền, để người chỉ có quyền của sales không thấy menu Nhân viên hay Quản lý Role.
  const visibleNavItems = NAV_ITEMS.filter((item) => {
    if (!item.requiredPermission) return true;
    return checkPermission(actor, item.requiredPermission);
  });

  return (
    <div style={{ display: 'flex', minHeight: '100vh', fontFamily: 'var(--font-sans)' }}>
      {/* Sidebar */}
      <aside
        style={{
          width: 250,
          backgroundColor: 'var(--color-surface)',
          borderRight: '1px solid var(--color-border)',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        <div
          style={{
            padding: 'var(--space-5)',
            borderBottom: '1px solid var(--color-border)',
          }}
        >
          <div style={{ fontSize: 'var(--text-lg)', fontWeight: 700, color: 'var(--color-brand)' }}>
            Pickory Admin
          </div>
          <div
            style={{
              fontSize: 'var(--text-xs)',
              color: 'var(--color-ink-muted)',
              marginTop: 'var(--space-1)',
            }}
          >
            Role: {actor.roles.join(', ') || 'Staff'}
          </div>
        </div>

        <nav style={{ flex: 1, padding: 'var(--space-3) 0' }}>
          {visibleNavItems.map((item) => {
            const isActive = location.pathname === item.path;
            return (
              <Link
                key={item.path}
                to={item.path}
                style={{
                  display: 'block',
                  padding: 'var(--space-3) var(--space-5)',
                  fontSize: 'var(--text-sm)',
                  fontWeight: isActive ? 600 : 400,
                  color: isActive ? 'var(--color-brand)' : 'var(--color-ink)',
                  backgroundColor: isActive ? 'var(--color-brand-soft)' : 'transparent',
                  textDecoration: 'none',
                }}
              >
                {item.label}
              </Link>
            );
          })}
        </nav>

        <div style={{ padding: 'var(--space-4)', borderTop: '1px solid var(--color-border)' }}>
          <button
            type="button"
            onClick={handleLogout}
            style={{
              width: '100%',
              padding: 'var(--space-2) var(--space-3)',
              backgroundColor: 'transparent',
              border: '1px solid var(--color-border)',
              borderRadius: 'var(--radius-control)',
              color: 'var(--color-ink)',
              fontSize: 'var(--text-sm)',
              cursor: 'pointer',
            }}
          >
            Đăng xuất
          </button>
        </div>
      </aside>

      {/* Main Content */}
      <main
        style={{
          flex: 1,
          backgroundColor: 'var(--color-surface-muted)',
          padding: 'var(--space-6)',
        }}
      >
        <Outlet />
      </main>
    </div>
  );
}
