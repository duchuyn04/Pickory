import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useActor } from '../features/identity/hooks/use-actor';
import { apiRequest } from './api';
import { Icon } from './icons';
import { ErrorAlert } from './error-alert';
import './header.css';

// Thanh đầu trang của khách theo `shop-retail/index.html`: nền đỏ, logo Pickory và thao tác theo phiên.
export function Header() {
  const { data: actor } = useActor();
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const [logoutError, setLogoutError] = useState<string | null>(null);

  async function handleLogout() {
    try {
      setLogoutError(null);
      await apiRequest('/api/auth/logout', { method: 'POST' });
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
        <Link to="/" className="site-logo">
          <span className="logo-mark site-logo-mark" aria-hidden="true">
            P
          </span>
          Pickory
        </Link>
        <nav className="site-nav" aria-label="Điều hướng chính">
          <Link to="/">Trang chủ</Link>
          <Link to="/privacy">Quyền riêng tư</Link>
        </nav>

        <div className="site-actions">
          {isCustomer && (
            <>
              <Link to="/me" className="site-action">
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
              <Link to="/admin" className="site-action">
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
              <Link to="/login" className="site-action">
                <Icon name="user" />
                Đăng nhập
              </Link>
              {/* Nút đăng ký nền trắng chữ đỏ, nổi bật trên thanh đỏ như nút chính của trang. */}
              <Link to="/register" className="site-action site-action-primary">
                Đăng ký
              </Link>
            </>
          )}
        </div>
      </div>

      {/* Lỗi đăng xuất nằm dưới thanh đỏ, vì chữ màu lỗi không đọc được trên nền đỏ. */}
      {logoutError && <ErrorAlert message={logoutError} className="site-header-alert" />}
    </header>
  );
}
