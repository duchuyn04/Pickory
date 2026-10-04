import { useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiRequest } from '../../../shared/api';
import { getEmailError, getPasswordError } from '../../../shared/identity-validation';
import { useActor } from '../use-actor';

export function StaffLoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { data: actor } = useActor();

  // Nếu đã đăng nhập staff thì chuyển thẳng vào /admin
  React.useEffect(() => {
    if (actor?.kind === 'staff') {
      navigate('/admin', { replace: true });
    }
  }, [actor, navigate]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const validationError = getEmailError(email) || getPasswordError(password, 1);
    if (validationError) {
      setError(validationError);
      return;
    }
    setLoading(true);

    try {
      await apiRequest('/api/admin/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email, password }),
      });
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      navigate('/admin', { replace: true });
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Đã có lỗi xảy ra. Vui lòng thử lại.');
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--color-surface-muted)',
        fontFamily: 'var(--font-sans)',
        padding: 'var(--space-4)',
      }}
    >
      <div
        style={{
          width: '100%',
          maxWidth: 420,
          backgroundColor: 'var(--color-surface)',
          borderRadius: 'var(--radius-card)',
          border: '1px solid var(--color-border)',
          padding: 'var(--space-6)',
          boxShadow: '0 4px 20px rgba(0,0,0,0.06)',
        }}
      >
        <div style={{ textAlign: 'center', marginBottom: 'var(--space-6)' }}>
          <h1
            style={{
              fontSize: 'var(--text-xl)',
              color: 'var(--color-brand)',
              margin: '0 0 var(--space-2) 0',
              fontWeight: 700,
            }}
          >
            Pickory Back-Office
          </h1>
          <p
            style={{
              color: 'var(--color-ink-muted)',
              fontSize: 'var(--text-sm)',
              margin: 0,
            }}
          >
            Đăng nhập dành cho nhân viên quản trị
          </p>
        </div>

        {error && (
          <div
            style={{
              backgroundColor: '#fee4e2',
              border: '1px solid var(--color-danger)',
              color: 'var(--color-danger)',
              padding: 'var(--space-3)',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-sm)',
              marginBottom: 'var(--space-4)',
            }}
          >
            {error}
          </div>
        )}

        <form
          noValidate
          onSubmit={handleSubmit}
          style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}
        >
          <div>
            <label
              htmlFor="email"
              style={{
                display: 'block',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                color: 'var(--color-ink)',
                marginBottom: 'var(--space-1)',
              }}
            >
              Email nhân viên
            </label>
            <input
              id="email"
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="nhanvien@pickory.local"
              style={{
                width: '100%',
                boxSizing: 'border-box',
                padding: 'var(--space-3)',
                borderRadius: 'var(--radius-control)',
                border: '1px solid var(--color-border)',
                fontSize: 'var(--text-md)',
                outline: 'none',
              }}
            />
          </div>

          <div>
            <label
              htmlFor="password"
              style={{
                display: 'block',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                color: 'var(--color-ink)',
                marginBottom: 'var(--space-1)',
              }}
            >
              Mật khẩu
            </label>
            <div style={{ position: 'relative' }}>
              <input
                id="password"
                type={showPassword ? 'text' : 'password'}
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Nhập mật khẩu"
                style={{
                  width: '100%',
                  boxSizing: 'border-box',
                  padding: 'var(--space-3)',
                  paddingRight: '60px',
                  borderRadius: 'var(--radius-control)',
                  border: '1px solid var(--color-border)',
                  fontSize: 'var(--text-md)',
                  outline: 'none',
                }}
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                style={{
                  position: 'absolute',
                  right: 'var(--space-2)',
                  top: '50%',
                  transform: 'translateY(-50%)',
                  background: 'none',
                  border: 'none',
                  color: 'var(--color-ink-muted)',
                  fontSize: 'var(--text-xs)',
                  cursor: 'pointer',
                  padding: 'var(--space-1) var(--space-2)',
                }}
              >
                {showPassword ? 'Ẩn' : 'Hiện'}
              </button>
            </div>
          </div>

          <button
            type="submit"
            disabled={loading}
            style={{
              backgroundColor: 'var(--color-brand)',
              color: '#ffffff',
              border: 'none',
              padding: 'var(--space-3)',
              borderRadius: 'var(--radius-control)',
              fontSize: 'var(--text-md)',
              fontWeight: 600,
              cursor: loading ? 'not-allowed' : 'pointer',
              opacity: loading ? 0.7 : 1,
              marginTop: 'var(--space-2)',
            }}
          >
            {loading ? 'Đang đăng nhập...' : 'Đăng nhập'}
          </button>
        </form>
      </div>
    </div>
  );
}
