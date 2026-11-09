import { Link } from 'react-router-dom';
import { APP_NAME } from './app-name';
import './footer.css';

// Chân trang dùng chung của khách: tên app bên trái, link chính sách bên phải.
export function Footer() {
  return (
    <footer className="site-footer">
      <div className="site-footer-inner">
        <div className="site-footer-brand">
          <span className="site-footer-name">{APP_NAME}</span>
          <span className="site-footer-copy">© 2026</span>
        </div>
        <nav className="site-footer-links" aria-label="Chân trang">
          <Link to="/support">Liên hệ CSKH</Link>
          <Link to="/policy/giao-hang">Phí giao hàng</Link>
          <Link to="/privacy">Quyền riêng tư</Link>
        </nav>
      </div>
    </footer>
  );
}
