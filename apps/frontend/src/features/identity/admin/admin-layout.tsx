import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Link, Navigate, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { BrandMark } from '../../../shared/brand-mark';
import { Icon } from '../../../shared/icons';
import { SupportEventsProvider } from '../../support-desk/admin/support-events-provider';
import { SupportTopBarTools } from '../../support-desk/admin/support-top-bar-tools';
import { WaitingSessionAlerts } from '../../support-desk/admin/waiting-session-alerts';
import { checkPermission, useActor, type ActorData } from '../hooks/use-actor';
import { ADMIN_NAV_GROUPS, filterNavGroupsByPermission, isNavItemActive } from './admin-navigation';
import './admin-layout.css';

const SIDEBAR_ID = 'admin-sidebar';
const COLLAPSED_STORAGE_KEY = 'pickory.admin.sidebarCollapsed';

// Trình duyệt có thể chặn localStorage (chế độ riêng tư). Khi đó menu mở rộng như mặc định.
function readStoredCollapsed(): boolean {
  try {
    return window.localStorage.getItem(COLLAPSED_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

// Lỗi ghi chỉ làm mất lựa chọn ở lần tải sau, không được làm hỏng thao tác thu gọn hiện tại.
function storeCollapsed(isCollapsed: boolean): void {
  try {
    window.localStorage.setItem(COLLAPSED_STORAGE_KEY, String(isCollapsed));
  } catch {
    return;
  }
}

// Chữ cái đầu của tên làm ảnh đại diện. Tài khoản chưa có tên dùng chữ đầu của email.
function readInitial(actor: ActorData): string {
  const source = actor.name?.trim() || actor.email?.trim() || 'P';
  return source.charAt(0).toUpperCase();
}

// Khung chung của khu vực quản trị: kiểm tra đăng nhập, vẽ menu theo quyền, chứa trang con.
export function AdminLayout() {
  const { data: actor, isLoading } = useActor();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  // Ngăn kéo menu chỉ dùng ở màn hẹp. CSS luôn hiện sidebar ở màn rộng.
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const [isCollapsed, setIsCollapsed] = useState(readStoredCollapsed);
  const menuButtonRef = useRef<HTMLButtonElement>(null);

  // Escape đóng ngăn kéo và trả focus về nút Menu, để người dùng bàn phím không mất vị trí.
  useEffect(() => {
    if (!isMenuOpen) return;
    function closeOnEscape(event: KeyboardEvent) {
      if (event.key !== 'Escape') return;
      setIsMenuOpen(false);
      menuButtonRef.current?.focus();
    }
    document.addEventListener('keydown', closeOnEscape);
    return () => document.removeEventListener('keydown', closeOnEscape);
  }, [isMenuOpen]);

  // Chưa biết ai đang đăng nhập thì chưa chuyển hướng, nếu không nhân viên đã đăng nhập bị đẩy ra trang login oan.
  if (isLoading) {
    return (
      <p role="status" className="admin-loading">
        Đang nạp dữ liệu...
      </p>
    );
  }

  // Khách vãng lai, khách hàng và nhân viên vừa mất phiên đều về trang đăng nhập nhân viên. Backend vẫn chặn dữ liệu bằng kiểm tra quyền.
  if (!actor || actor.kind !== 'staff') {
    return <Navigate to="/admin/login" replace />;
  }

  async function handleLogout() {
    try {
      await apiRequest('/api/auth/sessions/current', { method: 'DELETE' });
    } finally {
      // Lỗi mạng khi đăng xuất vẫn phải nạp lại actor và về trang đăng nhập, không kẹt ở khu vực quản trị.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      navigate('/admin/login', { replace: true });
    }
  }

  function toggleCollapsed() {
    const nextCollapsed = !isCollapsed;
    setIsCollapsed(nextCollapsed);
    // Lưu lựa chọn để lần tải trang sau sidebar giữ đúng độ rộng người dùng đã chọn.
    storeCollapsed(nextCollapsed);
  }

  const visibleGroups = filterNavGroupsByPermission(ADMIN_NAV_GROUPS, actor);
  const roleLabel = actor.roles.join(', ') || 'staff';
  // Thanh trên cùng ở màn rộng chỉ hiện khi có công cụ CSKH để chứa. Người khác vẫn thấy thanh này ở màn hẹp như cũ.
  const hasSupportTools = checkPermission(actor, 'conversations:read');
  // Việc báo khách vào hàng chờ chỉ chạy khi nhân viên "Sẵn sàng". Chỉ người có `conversations:reply` bật được trạng thái đó.
  const canReceiveWaitingAlerts = checkPermission(actor, 'conversations:reply');

  return (
    // Provider nằm ngoài `admin-shell` để luồng SSE sống suốt khu quản trị, kể cả khi nhân viên đổi trang.
    <SupportEventsProvider>
      {canReceiveWaitingAlerts && <WaitingSessionAlerts />}
      <div className="admin-shell" data-collapsed={isCollapsed}>
        <aside
          id={SIDEBAR_ID}
          className="admin-sidebar"
          data-open={isMenuOpen}
          aria-label="Menu quản trị"
        >
          <div className="admin-brand">
            <BrandMark />
            <span className="admin-brand-text admin-nav-label">
              <b>Pickory</b>
              <small>Trang quản trị</small>
            </span>
          </div>

          <nav className="admin-nav" aria-label="Quản trị">
            {visibleGroups.map((group) => (
              <div className="admin-nav-group" key={group.label ?? 'chung'}>
                {group.label && (
                  <p className="admin-nav-group-label" data-nav-group-label>
                    {group.label}
                  </p>
                )}
                <ul className="admin-nav-list">
                  {group.items.map((item) => (
                    <li key={item.path}>
                      {/* Mục cha vẫn sáng ở trang con như /admin/products/:id. */}
                      <Link
                        to={item.path}
                        aria-current={isNavItemActive(item, pathname) ? 'page' : undefined}
                        className="admin-nav-link"
                        title={isCollapsed ? item.label : undefined}
                        // Đóng ngăn kéo sau khi chọn trang, để nội dung trang mới không bị lớp phủ che.
                        onClick={() => setIsMenuOpen(false)}
                      >
                        <Icon name={item.icon} />
                        <span className="admin-nav-label">{item.label}</span>
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </nav>

          <div className="admin-side-foot">
            <div className="admin-user">
              <span className="admin-avatar" aria-hidden="true">
                {readInitial(actor)}
              </span>
              <span className="admin-user-text admin-nav-label">
                <b>{actor.name || actor.email || 'Nhân viên'}</b>
                <small>Role: {roleLabel}</small>
              </span>
              <button
                type="button"
                className="admin-icon-button"
                aria-label="Đăng xuất"
                title="Đăng xuất"
                onClick={handleLogout}
              >
                <Icon name="logout" />
              </button>
            </div>
            {/* Thu gọn chỉ có ở màn rộng. Ở màn hẹp, sidebar đã là ngăn kéo ẩn sẵn. */}
            <button
              type="button"
              className="admin-collapse"
              aria-label="Thu gọn menu"
              aria-pressed={isCollapsed}
              onClick={toggleCollapsed}
            >
              <Icon name="chevronLeft" />
              <span className="admin-nav-label" aria-hidden="true">
                Thu gọn
              </span>
            </button>
          </div>
        </aside>

        {/* Lớp phủ chặn thao tác với trang phía sau khi ngăn kéo mở; bấm vào thì đóng ngăn kéo. */}
        {isMenuOpen && (
          <div className="admin-scrim" data-admin-scrim onClick={() => setIsMenuOpen(false)} />
        )}

        <div className="admin-body">
          <header className="admin-top" data-has-tools={hasSupportTools}>
            {/* `aria-expanded` và `aria-controls` báo cho trình đọc màn hình trạng thái của ngăn kéo menu. */}
            <button
              ref={menuButtonRef}
              type="button"
              className="admin-icon-button admin-menu-toggle"
              aria-label="Menu"
              aria-expanded={isMenuOpen}
              aria-controls={SIDEBAR_ID}
              onClick={() => setIsMenuOpen((wasOpen) => !wasOpen)}
            >
              <Icon name={isMenuOpen ? 'close' : 'menu'} />
            </button>
            <span className="admin-top-brand">
              <BrandMark />
              <b>Pickory</b>
            </span>
            <SupportTopBarTools />
          </header>
          <main className="admin-main">
            <Outlet />
          </main>
        </div>
      </div>
    </SupportEventsProvider>
  );
}
