import { useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { MiniCart } from '../features/commerce/customer/mini-cart';
import { useActor } from '../features/identity/hooks/use-actor';
import { apiRequest } from './api';
import { BrandMark } from './brand-mark';
import { CategoryMenu } from './category-menu';
import { ErrorAlert } from './error-alert';
import { Icon } from './icons';
import { useDialogFocus } from './use-dialog-focus';
import { getErrorMessage } from './error-message';
import './header.css';

const MENU_PANEL_ID = 'site-menu-panel';
// Ngưỡng khớp `--layout-breakpoint-desktop` trong `tokens.css` để rút gọn chữ gợi ý trên màn hẹp.
const DESKTOP_QUERY = '(min-width: 1200px)';

function readDesktopMatch(): boolean {
  // Môi trường test jsdom không cài `matchMedia`, nên mặc định dùng gợi ý đầy đủ của máy tính.
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return true;
  return window.matchMedia(DESKTOP_QUERY).matches;
}

// Thanh đầu trang hai tầng của khách: thanh tiện ích ở trên và thanh chính ở dưới.
export function Header() {
  const { data: actor } = useActor();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { pathname } = useLocation();

  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [isDesktop, setIsDesktop] = useState(readDesktopMatch);
  const [logoutError, setLogoutError] = useState<string | null>(null);
  const menuButtonRef = useRef<HTMLButtonElement>(null);

  // Theo dõi ngưỡng máy tính để đổi chữ gợi ý tìm kiếm và đóng ngăn menu khi mở rộng màn hình.
  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return;
    const mediaQuery = window.matchMedia(DESKTOP_QUERY);
    setIsDesktop(mediaQuery.matches);
    const handleChange = (event: MediaQueryListEvent) => {
      setIsDesktop(event.matches);
      // Đóng ngăn menu khi vượt ngưỡng 1200px để thanh điều hướng không giữ trạng thái hộp thoại.
      if (event.matches) setIsMenuOpen(false);
    };
    mediaQuery.addEventListener('change', handleChange);
    return () => mediaQuery.removeEventListener('change', handleChange);
  }, []);

  // Đóng ngăn menu khi chuyển trang, để khách không phải bấm đóng thêm lần nữa.
  useEffect(() => {
    setIsMenuOpen(false);
  }, [pathname]);

  function closeMenu() {
    setIsMenuOpen(false);
    // Trả focus tường minh về nút mở ngăn menu sau khi đóng.
    menuButtonRef.current?.focus();
  }

  const panelRef = useDialogFocus({ isOpen: isMenuOpen, onClose: closeMenu });

  function handleSearchSubmit(event: FormEvent<HTMLFormElement>) {
    // Chặn điều hướng mặc định vì API sản phẩm hiện chưa hỗ trợ lọc theo từ khóa.
    event.preventDefault();
  }

  async function handleLogout() {
    try {
      // Đóng ngăn menu ngay khi chọn đăng xuất để lộ thông báo lỗi nếu có.
      setIsMenuOpen(false);
      setLogoutError(null);
      await apiRequest('/api/auth/sessions/current', { method: 'DELETE' });
    } catch (err: unknown) {
      setLogoutError(getErrorMessage(err, 'Đăng xuất thất bại.'));
    } finally {
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      navigate('/');
    }
  }

  const isCustomer = actor?.kind === 'customer';
  const isStaff = actor?.kind === 'staff';
  const searchPlaceholder = isDesktop ? 'Bạn muốn tìm sản phẩm nào?' : 'Tìm sản phẩm';

  return (
    <header className="site-header">
      <div className="site-header-inner">
        {/* Hai cam kết ở bên trái thanh tiện ích trên màn máy tính. */}
        <ul className="site-header-info">
          <li>
            <Icon name="shield" />
            Bảo hành ghi rõ trên từng sản phẩm
          </li>
          <li>
            <Icon name="truck" />
            Phí giao hàng tính theo địa chỉ nhận
          </li>
        </ul>

        {/* Nút mở ngăn menu hiện dưới 1200px để khách mở tài khoản và liên kết khi chưa tới cỡ máy tính. */}
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

        {/* Tên truy cập đặt qua `aria-label` để màn điện thoại ẩn chữ vẫn đọc đúng tên thương hiệu. */}
        <Link
          to="/"
          className="site-logo"
          aria-label="Pickory"
          onClick={() => setIsMenuOpen(false)}
        >
          <BrandMark className="site-logo-mark" />
          <span className="site-logo-text" aria-hidden="true">
            Pickory
          </span>
        </Link>

        {/* Nút Danh mục hiện từ 768px. */}
        <CategoryMenu />

        {/* Ô tìm kiếm đặt trong thanh chính. Nhãn truy cập đặt ở ô nhập để không trùng với landmark. */}
        <form role="search" className="site-search" onSubmit={handleSearchSubmit}>
          <Icon name="search" className="icon site-search-icon" />
          <input
            type="search"
            className="site-search-input"
            value={searchQuery}
            onChange={(event) => setSearchQuery(event.target.value)}
            placeholder={searchPlaceholder}
            aria-label="Tìm kiếm sản phẩm"
          />
        </form>

        {/* Ngăn menu: trên máy tính tách vào hai hàng lưới, dưới 1200px là ngăn kéo hộp thoại. */}
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
              Tra cứu đơn hàng
            </Link>
            <Link to="/policy/giao-hang" onClick={() => setIsMenuOpen(false)}>
              Phí giao hàng
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

        {/* Giỏ hàng nằm ở góc phải thanh chính trên mọi cỡ màn hình. */}
        <div className="site-header-cart">
          <MiniCart />
        </div>
      </div>

      {/* Lớp phủ màn hình khi ngăn menu mở dưới 1200px; bấm vào lớp phủ thì đóng ngăn. */}
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
