import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useActor } from '../features/identity/hooks/use-actor';
import { apiRequest } from './api';

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
    <header
      style={{
        backgroundColor: 'var(--color-surface)',
        borderBottom: '1px solid var(--color-border)',
        padding: 'var(--space-3) var(--space-6)',
        display: 'flex',
        flexWrap: 'wrap',
        gap: 'var(--space-3)',
        alignItems: 'center',
        justifyContent: 'space-between',
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          flexWrap: 'wrap',
          maxWidth: '100%',
          gap: 'var(--space-3)',
        }}
      >
        <Link
          to="/"
          style={{
            fontSize: 'var(--text-xl)',
            fontWeight: 800,
            color: 'var(--color-brand)',
            textDecoration: 'none',
          }}
        >
          Pickory
        </Link>
        <nav style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-4)' }}>
          <Link
            to="/"
            style={{
              color: 'var(--color-ink)',
              textDecoration: 'none',
              fontSize: 'var(--text-sm)',
              fontWeight: 500,
            }}
          >
            Trang chủ
          </Link>
          <Link
            to="/privacy"
            style={{
              color: 'var(--color-ink-muted)',
              textDecoration: 'none',
              fontSize: 'var(--text-sm)',
              fontWeight: 500,
            }}
          >
            Quyền riêng tư
          </Link>
        </nav>
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 'var(--space-3)',
        }}
      >
        {logoutError && (
          <span style={{ color: 'var(--color-danger)', fontSize: 'var(--text-xs)' }}>
            {logoutError}
          </span>
        )}
        {isCustomer && (
          <>
            <Link
              to="/me"
              style={{
                color: 'var(--color-ink)',
                textDecoration: 'none',
                fontSize: 'var(--text-sm)',
                fontWeight: 500,
              }}
            >
              Tài khoản
            </Link>
            <button
              type="button"
              onClick={handleLogout}
              style={{
                background: 'none',
                border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-control)',
                padding: 'var(--space-2) var(--space-3)',
                color: 'var(--color-ink)',
                fontSize: 'var(--text-sm)',
                cursor: 'pointer',
              }}
            >
              Đăng xuất
            </button>
          </>
        )}

        {isStaff && (
          <>
            <Link
              to="/admin"
              style={{
                color: 'var(--color-brand)',
                textDecoration: 'none',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
              }}
            >
              Back-Office
            </Link>
            <button
              type="button"
              onClick={handleLogout}
              style={{
                background: 'none',
                border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-control)',
                padding: 'var(--space-2) var(--space-3)',
                color: 'var(--color-ink)',
                fontSize: 'var(--text-sm)',
                cursor: 'pointer',
              }}
            >
              Đăng xuất
            </button>
          </>
        )}

        {!isCustomer && !isStaff && (
          <>
            <Link
              to="/login"
              style={{
                color: 'var(--color-ink)',
                textDecoration: 'none',
                fontSize: 'var(--text-sm)',
                fontWeight: 500,
                padding: 'var(--space-2) var(--space-3)',
              }}
            >
              Đăng nhập
            </Link>
            <Link
              to="/register"
              style={{
                backgroundColor: 'var(--color-brand)',
                color: '#ffffff',
                textDecoration: 'none',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                padding: 'var(--space-2) var(--space-4)',
                borderRadius: 'var(--radius-control)',
              }}
            >
              Đăng ký
            </Link>
          </>
        )}
      </div>
    </header>
  );
}
