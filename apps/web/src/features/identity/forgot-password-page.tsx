import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { apiRequest } from '../../shared/api';
import { getEmailError } from '../../shared/identity-validation';

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    // Kiểm tra định dạng email ngay tại client để phản hồi tức thì mà không cần gửi request lên server.
    const validationError = getEmailError(email);
    if (validationError) {
      setError(validationError);
      return;
    }
    setLoading(true);

    try {
      await apiRequest('/api/auth/forgot-password', {
        method: 'POST',
        body: JSON.stringify({ email }),
      });
      // Chuyển sang giao diện xác nhận chung dù email có tồn tại hay không nhằm ngăn chặn dò quét tài khoản.
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
            Quên mật khẩu
          </h1>
          <p
            style={{
              color: 'var(--color-ink-muted)',
              fontSize: 'var(--text-sm)',
              margin: '0 0 var(--space-5) 0',
              textAlign: 'center',
            }}
          >
            Nhập email để nhận liên kết đặt lại mật khẩu
          </p>

          {submitted ? (
            <div style={{ textAlign: 'center' }}>
              <div
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
                Nếu email tồn tại trong hệ thống, hướng dẫn đặt lại mật khẩu đã được gửi đến hộp thư
                của bạn.
              </div>
              <Link
                to="/login"
                style={{
                  color: 'var(--color-brand)',
                  textDecoration: 'none',
                  fontWeight: 600,
                  fontSize: 'var(--text-sm)',
                }}
              >
                ← Quay lại trang đăng nhập
              </Link>
            </div>
          ) : (
            <>
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
                    htmlFor="forgot-email"
                    style={{
                      display: 'block',
                      fontSize: 'var(--text-sm)',
                      fontWeight: 600,
                      color: 'var(--color-ink)',
                      marginBottom: 'var(--space-1)',
                    }}
                  >
                    Email tài khoản
                  </label>
                  <input
                    id="forgot-email"
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
                  {loading ? 'Đang gửi yêu cầu...' : 'Gửi liên kết đặt lại'}
                </button>
              </form>

              <div
                style={{
                  marginTop: 'var(--space-5)',
                  textAlign: 'center',
                  fontSize: 'var(--text-sm)',
                }}
              >
                <Link
                  to="/login"
                  style={{ color: 'var(--color-ink-muted)', textDecoration: 'none' }}
                >
                  ← Quay lại đăng nhập
                </Link>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
