import React, { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '../../shared/api';
import { getPasswordError } from '../../shared/identity-validation';

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  // Lưu token ban đầu từ URL vào state để sau khi xóa khỏi URL vẫn giữ được giá trị
  const [token] = useState(() => searchParams.get('token') || '');

  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [userKind, setUserKind] = useState<'customer' | 'staff' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  // Xóa token khỏi URL sau khi đã nhận để bảo mật token dùng một lần
  useEffect(() => {
    if (searchParams.has('token')) {
      navigate(location.pathname, { replace: true });
    }
  }, [searchParams, location.pathname, navigate]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    const passwordError = getPasswordError(newPassword, 8);
    if (passwordError) {
      setError(passwordError);
      return;
    }

    if (!confirmPassword) {
      setError('Vui lòng xác nhận lại mật khẩu.');
      return;
    }

    if (newPassword !== confirmPassword) {
      setError('Mật khẩu xác nhận không khớp.');
      return;
    }

    setLoading(true);

    try {
      const res = await apiRequest<{ success: boolean; userKind: 'customer' | 'staff' }>(
        '/api/auth/reset-password',
        {
          method: 'POST',
          body: JSON.stringify({ token, newPassword }),
        },
      );

      // Đặt lại mật khẩu thu hồi mọi phiên, nên bỏ actor cũ và nạp lại trước khi hiện link đăng nhập.
      await queryClient.resetQueries({ queryKey: ['actor'], exact: true });

      setUserKind(res.userKind);
      setSubmitted(true);
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

  if (!token) {
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
              maxWidth: 420,
              backgroundColor: 'var(--color-surface)',
              borderRadius: 'var(--radius-card)',
              border: '1px solid var(--color-border)',
              padding: 'var(--space-6)',
              boxShadow: '0 4px 20px rgba(0,0,0,0.06)',
              textAlign: 'center',
            }}
          >
            <h1
              style={{
                fontSize: 'var(--text-xl)',
                color: 'var(--color-ink)',
                margin: '0 0 var(--space-2) 0',
                fontWeight: 700,
              }}
            >
              Đặt lại mật khẩu
            </h1>
            <div
              role="alert"
              style={{
                backgroundColor: '#fee4e2',
                border: '1px solid var(--color-danger)',
                color: 'var(--color-danger)',
                padding: 'var(--space-4)',
                borderRadius: 'var(--radius-control)',
                fontSize: 'var(--text-sm)',
                margin: 'var(--space-4) 0',
              }}
            >
              Mã token không hợp lệ hoặc thiếu trong liên kết. Vui lòng kiểm tra lại liên kết trong
              email của bạn.
            </div>
            <Link
              to="/forgot-password"
              style={{
                color: 'var(--color-brand)',
                textDecoration: 'none',
                fontWeight: 600,
                fontSize: 'var(--text-sm)',
              }}
            >
              ← Yêu cầu đặt lại mật khẩu mới
            </Link>
          </div>
        </div>
      </div>
    );
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
            maxWidth: 420,
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
            Đặt lại mật khẩu
          </h1>
          <p
            style={{
              color: 'var(--color-ink-muted)',
              fontSize: 'var(--text-sm)',
              margin: '0 0 var(--space-5) 0',
              textAlign: 'center',
            }}
          >
            Nhập mật khẩu mới cho tài khoản của bạn
          </p>

          {submitted ? (
            <div style={{ textAlign: 'center' }}>
              <div
                role="status"
                style={{
                  backgroundColor: '#e6f4ea',
                  border: '1px solid var(--color-success)',
                  color: 'var(--color-success)',
                  padding: 'var(--space-4)',
                  borderRadius: 'var(--radius-control)',
                  fontSize: 'var(--text-sm)',
                  marginBottom: 'var(--space-5)',
                }}
              >
                Mật khẩu của bạn đã được cập nhật thành công! Mọi phiên đăng nhập cũ đã được đăng
                xuất an toàn.
              </div>
              <Link
                to={userKind === 'staff' ? '/admin/login' : '/login'}
                style={{
                  display: 'inline-block',
                  backgroundColor: 'var(--color-brand)',
                  color: '#ffffff',
                  textDecoration: 'none',
                  fontWeight: 600,
                  fontSize: 'var(--text-sm)',
                  padding: 'var(--space-3) var(--space-5)',
                  borderRadius: 'var(--radius-control)',
                }}
              >
                {userKind === 'staff' ? 'Đăng nhập trang quản trị' : 'Đăng nhập ngay'}
              </Link>
            </div>
          ) : (
            <>
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
                    htmlFor="reset-password"
                    style={{
                      display: 'block',
                      fontSize: 'var(--text-sm)',
                      fontWeight: 600,
                      color: 'var(--color-ink)',
                      marginBottom: 'var(--space-1)',
                    }}
                  >
                    Mật khẩu mới (tối thiểu 8 ký tự)
                  </label>
                  <div style={{ position: 'relative' }}>
                    <input
                      id="reset-password"
                      type={showPassword ? 'text' : 'password'}
                      required
                      value={newPassword}
                      onChange={(e) => setNewPassword(e.target.value)}
                      placeholder="Nhập mật khẩu mới"
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
                    htmlFor="reset-confirm"
                    style={{
                      display: 'block',
                      fontSize: 'var(--text-sm)',
                      fontWeight: 600,
                      color: 'var(--color-ink)',
                      marginBottom: 'var(--space-1)',
                    }}
                  >
                    Xác nhận mật khẩu mới
                  </label>
                  <input
                    id="reset-confirm"
                    type={showPassword ? 'text' : 'password'}
                    required
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    placeholder="Nhập lại mật khẩu mới"
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
                  {loading ? 'Đang cập nhật...' : 'Cập nhật mật khẩu'}
                </button>
              </form>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
