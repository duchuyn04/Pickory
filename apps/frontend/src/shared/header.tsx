import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { MiniCart } from '../features/commerce/customer/mini-cart';
import { useActor } from '../features/identity/hooks/use-actor';
import { apiRequest } from './api';
import { BrandMark } from './brand-mark';
import { ErrorAlert } from './error-alert';
import { Icon } from './icons';
import { useDialogFocus } from './use-dialog-focus';
import './header.css';

const MENU_PANEL_ID = 'site-menu-panel';

// Thanh đầu trang của khách theo `shop-retail/index.html`: nền đỏ, logo Pickory và thao tác theo phiên.
export function Header() {
  const { data: actor } = useActor();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { pathname } = useLocation();

  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const [logoutError, setLogoutError] = useState<string | null>(null);
  const menuButtonRef = useRef<HTMLButtonElement>(null);

  // Đóng ngăn kéo khi chuyển sang trang khác, để người dùng không phải bấm đóng thêm lần nữa.
  useEffect(() => {
    setIsMenuOpen(false);
  }, [pathname]);

  function closeMenu() {
    setIsMenuOpen(false);
    // Trả focus tường minh về nút mở menu sau khi đóng.
    menuButtonRef.current?.focus();
  }

  const panelRef = useDialogFocus({ isOpen: isMenuOpen, onClose: closeMenu });

  async function handleLogout() {
    try {
      // Đóng ngăn kéo ngay khi chọn đăng xuất để lộ thông báo lỗi nếu có.
      setIsMenuOpen(false);
      setLogoutError(null);
      await apiRequest('/api/auth/sessions/current', { method: 'DELETE' });
    } catch (err: unknown) {
      setLogoutError(err instanceof Error ? err.message : 'Đăng xuất thất bại.');
    } finally {
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      navigate('/');
    }
  }

  const isCustomer = actor?.kind === 'customer';
  const isStaff = actor?.kind === 'staff';

  return (
    <header className="site-header">
      <div className="site-header-inner">
        {/* Nút menu chỉ hiện ở màn hình hẹp (dưới 768px). */}
        <button
          ref={menuButtonRef}
          type="button"
          className="site-menu-toggle"
          aria-label="Menu"
          aria-expanded={isMenuOpen}
          aria-controls={MENU_PANEL_ID}
          onClick={() => setIsMenuOpen((current) => !current)}
        >
          <Icon name={isMenuOpen ? 'close' : 'menu'} />
        </button>

        <Link to="/" className="site-logo" onClick={() => setIsMenuOpen(false)}>
          <BrandMark className="site-logo-mark" />
          Pickory
        </Link>

        {/* Ngăn menu: trên màn rộng là thanh điều hướng thông thường, trên mobile là ngăn kéo hộp thoại. */}
        <div
          id={MENU_PANEL_ID}
          ref={panelRef}
          className="site-menu-panel"
          data-open={isMenuOpen}
          role={isMenuOpen ? 'dialog' : undefined}
          aria-modal={isMenuOpen ? 'true' : undefined}
          aria-label={isMenuOpen ? 'Menu điều hướng' : undefined}
          tabIndex={-1}
        >
          <div className="site-menu-head">
            <span className="site-menu-title">Menu</span>
            <button
              type="button"
              className="site-menu-close"
              aria-label="Đóng menu"
              onClick={closeMenu}
            >
              <Icon name="close" />
            </button>
          </div>

          <nav className="site-nav" aria-label="Điều hướng chính">
            <Link to="/" onClick={() => setIsMenuOpen(false)}>
              Trang chủ
            </Link>
            {/* Khách vãng lai chưa có tài khoản vẫn cần đường vào trang tra cứu đơn từ mọi trang. */}
            <Link to="/order-lookup" onClick={() => setIsMenuOpen(false)}>
              Tra cứu đơn
            </Link>
            <Link to="/privacy" onClick={() => setIsMenuOpen(false)}>
              Quyền riêng tư
            </Link>
          </nav>

          <div className="site-actions">
            {isCustomer && (
              <>
                <Link to="/me" className="site-action" onClick={() => setIsMenuOpen(false)}>
                  <Icon name="user" />
                  Tài khoản
                </Link>
                <button type="button" className="site-action" onClick={handleLogout}>
                  <Icon name="logout" />
                  Đăng xuất
                </button>
              </>
            )}

            {isStaff && (
              <>
                <Link to="/admin" className="site-action" onClick={() => setIsMenuOpen(false)}>
                  <Icon name="dashboard" />
                  Back-Office
                </Link>
                <button type="button" className="site-action" onClick={handleLogout}>
                  <Icon name="logout" />
                  Đăng xuất
                </button>
              </>
            )}

            {!isCustomer && !isStaff && (
              <>
                <Link to="/login" className="site-action" onClick={() => setIsMenuOpen(false)}>
                  <Icon name="user" />
                  Đăng nhập
                </Link>
                {/* Nút đăng ký nổi bật để khách dễ nhận biết thao tác tạo tài khoản. */}
                <Link
                  to="/register"
                  className="site-action site-action-primary"
                  onClick={() => setIsMenuOpen(false)}
                >
                  Đăng ký
                </Link>
              </>
            )}
          </div>
        </div>

        {/* Giỏ hàng nằm ở góc phải thanh đầu trang ở cả desktop và mobile. */}
        <div className="site-header-cart">
          <MiniCart />
        </div>
      </div>

      {/* Lớp phủ màn hình khi ngăn menu mở trên mobile; bấm vào lớp phủ thì đóng ngăn. */}
      {isMenuOpen && (
        <div
          className="site-menu-scrim"
          data-site-menu-scrim
          onClick={closeMenu}
          aria-hidden="true"
        />
      )}

      {/* Lỗi đăng xuất nằm dưới thanh đỏ, vì chữ màu lỗi không đọc được trên nền đỏ. */}
      {logoutError && <ErrorAlert message={logoutError} className="site-header-alert" />}
    </header>
  );
}
