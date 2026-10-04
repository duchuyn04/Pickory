import { useQueryClient } from '@tanstack/react-query';
import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { apiRequest } from '../../shared/api';
import { getEmailError, getPasswordError } from '../../shared/identity-validation';
import { useActor } from './use-actor';

// Trang đăng ký tài khoản khách bằng email và mật khẩu.
export function RegisterPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  // Giá trị của ô bẫy bot (honeypot): người thật không thấy ô này nên luôn rỗng.
  const [website, setWebsite] = useState('');
  // Cho người dùng bật xem mật khẩu để kiểm tra gõ đúng.
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { data: actor } = useActor();

  React.useEffect(() => {
    // Khách đã đăng nhập thì không cần đăng ký nữa, đưa về trang chủ.
    if (actor?.kind === 'customer') {
      navigate('/', { replace: true });
    }
  }, [actor, navigate]);

  async function handleSubmit(e: React.FormEvent) {
    // Chặn trình duyệt tải lại trang khi gửi form.
    e.preventDefault();
    setError(null);

    // Kiểm tra ở trình duyệt để báo lỗi tiếng Việt ngay và không tốn một trong 5 lượt đăng ký mỗi giờ của IP này.
    const validationError = getEmailError(email) || getPasswordError(password);
    if (validationError) {
      setError(validationError);
      return;
    }

    setLoading(true);

    try {
      await apiRequest('/api/auth/register', {
        method: 'POST',
        // Trường để trống không gửi (undefined) thay vì gửi chuỗi rỗng.
        // website luôn rỗng với người thật; có giá trị thì server nhận ra là bot và từ chối.
        body: JSON.stringify({
          email,
          password,
          name: name || undefined,
          phone: phone || undefined,
          website: website || undefined,
        }),
      });
      // Server đăng nhập luôn sau khi đăng ký, nên nạp lại actor để header hiện trạng thái đã đăng nhập.
      await queryClient.invalidateQueries({ queryKey: ['actor'] });
      // replace để nút Quay lại không đưa người dùng về trang đăng ký.
      navigate('/', { replace: true });
    } catch (err: unknown) {
      // Hiện nguyên thông báo của API (email đã dùng, quá nhiều lần đăng ký...) vì đã là tiếng Việt.
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
        backgroundColor: 'var(--color-surface-muted)',
        fontFamily: 'var(--font-sans)',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: 'var(--space-7) var(--space-4)',
        }}
      >
        <div
          style={{
            width: '100%',
            maxWidth: 440,
            backgroundColor: 'var(--color-surface)',
            borderRadius: 'var(--radius-card)',
            border: '1px solid var(--color-border)',
            padding: 'var(--space-6)',
            boxShadow: '0 4px 20px rgba(0,0,0,0.06)',
          }}
        >
          <h1
            style={{
              fontSize: 'var(--text-xl)',
              color: 'var(--color-ink)',
              margin: '0 0 var(--space-2) 0',
              fontWeight: 700,
              textAlign: 'center',
            }}
          >
            Đăng ký tài khoản
          </h1>
          <p
            style={{
              color: 'var(--color-ink-muted)',
              fontSize: 'var(--text-sm)',
              margin: '0 0 var(--space-5) 0',
              textAlign: 'center',
            }}
          >
            Tạo tài khoản để quản lý đơn hàng và nhận ưu đãi
          </p>

          {error && (
            <div
              role="alert"
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
                htmlFor="reg-email"
                style={{
                  display: 'block',
                  fontSize: 'var(--text-sm)',
                  fontWeight: 600,
                  color: 'var(--color-ink)',
                  marginBottom: 'var(--space-1)',
                }}
              >
                Email *
              </label>
              <input
                id="reg-email"
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="ban@example.com"
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
                htmlFor="reg-password"
                style={{
                  display: 'block',
                  fontSize: 'var(--text-sm)',
                  fontWeight: 600,
                  color: 'var(--color-ink)',
                  marginBottom: 'var(--space-1)',
                }}
              >
                Mật khẩu * (tối thiểu 8 ký tự)
              </label>
              <div style={{ position: 'relative' }}>
                <input
                  id="reg-password"
                  type={showPassword ? 'text' : 'password'}
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Nhập tối thiểu 8 ký tự"
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

            <div>
              <label
                htmlFor="reg-name"
                style={{
                  display: 'block',
                  fontSize: 'var(--text-sm)',
                  fontWeight: 600,
                  color: 'var(--color-ink)',
                  marginBottom: 'var(--space-1)',
                }}
              >
                Họ và tên (tùy chọn)
              </label>
              <input
                id="reg-name"
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Nguyễn Văn A"
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
                htmlFor="reg-phone"
                style={{
                  display: 'block',
                  fontSize: 'var(--text-sm)',
                  fontWeight: 600,
                  color: 'var(--color-ink)',
                  marginBottom: 'var(--space-1)',
                }}
              >
                Số điện thoại (tùy chọn)
              </label>
              <input
                id="reg-phone"
                type="tel"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                placeholder="0912 345 678"
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

            {/* Honeypot: ô ẩn để bẫy bot tự động điền form, ẩn hoàn toàn với người dùng và trình đọc */}
            <div
              style={{
                display: 'none',
              }}
              aria-hidden="true"
            >
              <label htmlFor="reg-website">Website</label>
              <input
                id="reg-website"
                type="text"
                name="website"
                value={website}
                onChange={(e) => setWebsite(e.target.value)}
                tabIndex={-1}
                autoComplete="off"
              />
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
              {loading ? 'Đang đăng ký...' : 'Tạo tài khoản'}
            </button>

            <div
              style={{
                textAlign: 'center',
                margin: 'var(--space-3) 0',
                color: 'var(--color-ink-muted)',
                fontSize: 'var(--text-xs)',
              }}
            >
              HOẶC
            </div>

            <a
              href="/api/auth/google/start"
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: 'var(--space-2)',
                backgroundColor: 'var(--color-surface)',
                color: 'var(--color-ink)',
                border: '1px solid var(--color-border)',
                padding: 'var(--space-3)',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-sm)',
                fontWeight: 600,
                textDecoration: 'none',
                cursor: 'pointer',
              }}
            >
              <svg width="18" height="18" viewBox="0 0 24 24">
                <path
                  fill="#4285F4"
                  d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z"
                />
                <path
                  fill="#34A853"
                  d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"
                />
                <path
                  fill="#FBBC05"
                  d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.06H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.94l2.85-2.22.81-.63z"
                />
                <path
                  fill="#EA4335"
                  d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.06l3.66 2.84c.87-2.6 3.3-4.52 6.16-4.52z"
                />
              </svg>
              Đăng ký bằng Google
            </a>
          </form>

          <div
            style={{ marginTop: 'var(--space-5)', textAlign: 'center', fontSize: 'var(--text-sm)' }}
          >
            <span style={{ color: 'var(--color-ink-muted)' }}>Đã có tài khoản? </span>
            <Link
              to="/login"
              style={{ color: 'var(--color-brand)', textDecoration: 'none', fontWeight: 600 }}
            >
              Đăng nhập ngay
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}
